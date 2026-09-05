using System.Reflection;
using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

internal sealed class SimpleMonitorSafetyGate
{
    private const string StateSetting = "SimpleMonitor.SafetyState.v1";
    internal static readonly TimeSpan MinimumSendGap = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    private static readonly SemaphoreSlim PhysicalSendGate = new(1, 1);
    private static readonly MethodInfo EvaluateMethod = typeof(ChromeDevToolsService)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "EvaluateAsync" && method.GetParameters().Length == 4);

    private const string RateLimitProbeExpression = """
(() => {
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none' && style.opacity !== '0';
  };
  const textOf = element => (element?.innerText || element?.textContent || '').trim();
  const pattern = /too many requests|making requests too quickly|temporarily limited access|temporarily limited access to your conversations|please wait a few minutes before trying again|http\s*429|error\s*429|status\s*429/i;
  const dismissPattern = /^(got it|ok|okay|dismiss|close|understood|حسنًا|حسنا|فهمت|إغلاق|اغلاق)$/i;
  const transcriptSelector = '[data-message-author-role], [data-testid^="conversation-turn"], article[data-testid^="conversation-turn"]';

  const hasDismissControl = root => [...root.querySelectorAll('button,[role="button"]')].some(button => {
    if (!visible(button)) return false;
    const label = `${textOf(button)} ${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
    return dismissPattern.test(label);
  });

  const modalRoot = element => {
    let node = element;
    for (let depth = 0; node && node !== document.body && depth < 10; depth++, node = node.parentElement) {
      if (!visible(node)) continue;
      if (node.closest(transcriptSelector)) return null;
      const style = getComputedStyle(node);
      const role = (node.getAttribute('role') || '').toLowerCase();
      const className = typeof node.className === 'string' ? node.className : '';
      const semanticModal = role === 'dialog'
        || role === 'alert'
        || node.getAttribute('aria-modal') === 'true'
        || node.getAttribute('data-state') === 'open'
        || node.hasAttribute('data-radix-portal')
        || /(^|\s|[-_])(modal|dialog)(\s|[-_]|$)/i.test(className);
      const dismiss = hasDismissControl(node);
      const rect = node.getBoundingClientRect();
      const overlayLike = style.position === 'fixed' || style.position === 'sticky';
      if ((semanticModal || dismiss || (overlayLike && dismiss)) && rect.width >= 220 && rect.height >= 70)
        return node;
    }
    return null;
  };

  const bodyText = textOf(document.body);
  if (!pattern.test(bodyText)) return '';

  const semanticSelectors = [
    '[role="dialog"]',
    '[aria-modal="true"]',
    '[role="alert"]',
    '[aria-live="assertive"]',
    '[data-state="open"]',
    '[data-radix-portal]'
  ];
  for (const selector of semanticSelectors) {
    for (const element of document.querySelectorAll(selector)) {
      if (!visible(element) || element.closest(transcriptSelector)) continue;
      const text = textOf(element);
      if (text && text.length <= 4000 && pattern.test(text)) return text;
    }
  }

  // Current ChatGPT can render the protection notice in a visually modal portal without
  // role=dialog/aria-modal. Search only visible short text nodes, then require modal evidence
  // (including the visible "Got it" control) so transcript text cannot create a false 429.
  for (const element of document.querySelectorAll('h1,h2,h3,h4,p,div,section,span')) {
    if (!visible(element) || element.closest(transcriptSelector)) continue;
    const text = textOf(element);
    if (!text || text.length > 1400 || !pattern.test(text)) continue;
    const root = modalRoot(element);
    if (!root) continue;
    const rootText = textOf(root);
    if (rootText && rootText.length <= 4000 && pattern.test(rootText)) return rootText;
    return text;
  }

  return '';
})()
""";

    private readonly LocalDatabase? _database;
    private readonly object _sync = new();
    private readonly DateTimeOffset _startupQuietUntilUtc = DateTimeOffset.UtcNow + MinimumSendGap;
    private DurableState _state = DurableState.Empty;
    private bool _initialized;

    internal SimpleMonitorSafetyGate(LocalDatabase? database)
    {
        _database = database;
    }

    internal async Task<SendPermit> AcquireSendPermitAsync(
        ChromeDevToolsService chrome,
        Func<CancellationToken, Task<ChromeTab>> tabResolver,
        Func<ChromeTab, CancellationToken, Task<ChatPageState>> stateReader,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await PhysicalSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var release = true;
        try
        {
            while (true)
            {
                await WaitForRateLimitClearAsync(chrome, tabResolver, status, cancellationToken).ConfigureAwait(false);
                await WaitForQuietWindowAsync(status, cancellationToken).ConfigureAwait(false);

                var tab = await tabResolver(cancellationToken).ConfigureAwait(false);
                var state = await stateReader(tab, cancellationToken).ConfigureAwait(false);
                if (state.IsGenerating)
                {
                    status?.Invoke("SEND GATE — ChatGPT is still generating. Waiting; no composer mutation will occur.");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var rateLimitText = await ProbeRateLimitTextAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (IsRateLimitText(rateLimitText))
                {
                    await ActivateRateLimitAsync(rateLimitText, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                release = false;
                return new SendPermit(this, tab, state);
            }
        }
        finally
        {
            if (release)
                PhysicalSendGate.Release();
        }
    }

    internal async Task<bool> ObserveRateLimitAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var text = await ProbeRateLimitTextAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
        if (!IsRateLimitText(text))
            return IsRateLimitActive;

        await ActivateRateLimitAsync(text, cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal async Task WaitForRateLimitClearAsync(
        ChromeDevToolsService chrome,
        Func<CancellationToken, Task<ChromeTab>> tabResolver,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            DurableState snapshot;
            lock (_sync) snapshot = _state;
            if (!snapshot.RateLimitActive)
                return;

            var now = DateTimeOffset.UtcNow;
            var retryAt = snapshot.RetryAtUtc ?? now + BackoffSchedule[Math.Clamp(snapshot.BackoffIndex, 0, BackoffSchedule.Length - 1)];
            if (now < retryAt)
            {
                var remaining = retryAt - now;
                status?.Invoke($"RATE LIMITED — global send pause {FormatRemaining(remaining)}. No message will be sent or retried.");
                await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var tab = await tabResolver(cancellationToken).ConfigureAwait(false);
                var visible = await ProbeRateLimitTextAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (IsRateLimitText(visible))
                {
                    await AdvanceRateLimitAsync(visible, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ClearRateLimitAsync(cancellationToken).ConfigureAwait(false);
                status?.Invoke("RATE LIMIT CLEARED — safe probe passed. Normal 15-second send gate remains enforced.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AdvanceRateLimitAsync($"safe probe unavailable: {ex.Message}", cancellationToken).ConfigureAwait(false);
                status?.Invoke("RATE LIMITED — safe probe failed. Backoff advanced fail-closed; no send is allowed.");
            }
        }
    }

    internal bool IsRateLimitActive
    {
        get { lock (_sync) return _state.RateLimitActive; }
    }

    internal static bool IsRateLimitText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("making requests too quickly", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("temporarily limited access", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("please wait a few minutes before trying again", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("http 429", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error 429", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("status 429", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_initialized) return;
        }

        DurableState loaded = DurableState.Empty;
        if (_database is not null)
        {
            var raw = await _database.GetSettingAsync(StateSetting, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    loaded = JsonSerializer.Deserialize<DurableState>(raw) ?? DurableState.Empty;
                }
                catch (JsonException)
                {
                    loaded = DurableState.Empty;
                }
            }
        }

        if (loaded.RateLimitActive && loaded.RetryAtUtc is null)
        {
            var index = Math.Clamp(loaded.BackoffIndex, 0, BackoffSchedule.Length - 1);
            loaded = loaded with { RetryAtUtc = DateTimeOffset.UtcNow + BackoffSchedule[index] };
        }

        lock (_sync)
        {
            if (_initialized) return;
            _state = loaded;
            _initialized = true;
        }

        if (_database is not null && loaded.RateLimitActive)
            await PersistAsync(loaded, cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForQuietWindowAsync(Action<string>? status, CancellationToken cancellationToken)
    {
        while (true)
        {
            DateTimeOffset notBefore;
            lock (_sync)
            {
                notBefore = _startupQuietUntilUtc;
                if (_state.LastPhysicalAttemptUtc is { } physical)
                    notBefore = Max(notBefore, physical + MinimumSendGap);
                if (_state.LastResponseCompletedUtc is { } completed)
                    notBefore = Max(notBefore, completed + MinimumSendGap);
            }

            var remaining = notBefore - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            status?.Invoke($"SEND GATE — safety quiet period {FormatRemaining(remaining)}. No physical send yet.");
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordPhysicalAttemptAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            next = _state with { LastPhysicalAttemptUtc = DateTimeOffset.UtcNow };
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task RecordResponseCompletedAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            next = _state with { LastResponseCompletedUtc = DateTimeOffset.UtcNow };
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ActivateRateLimitAsync(string text, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            if (_state.RateLimitActive)
            {
                next = _state with { LastRateLimitText = Compact(text) };
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                next = _state with
                {
                    RateLimitActive = true,
                    BackoffIndex = 0,
                    DetectedAtUtc = now,
                    RetryAtUtc = now + BackoffSchedule[0],
                    LastRateLimitText = Compact(text)
                };
            }
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task AdvanceRateLimitAsync(string text, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            var index = Math.Min(Math.Max(0, _state.BackoffIndex) + 1, BackoffSchedule.Length - 1);
            var now = DateTimeOffset.UtcNow;
            next = _state with
            {
                RateLimitActive = true,
                BackoffIndex = index,
                DetectedAtUtc = _state.DetectedAtUtc ?? now,
                RetryAtUtc = now + BackoffSchedule[index],
                LastRateLimitText = Compact(text)
            };
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ClearRateLimitAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            next = _state with
            {
                RateLimitActive = false,
                BackoffIndex = 0,
                RetryAtUtc = null,
                LastRateLimitText = string.Empty
            };
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    private Task PersistAsync(DurableState state, CancellationToken cancellationToken)
        => _database is null
            ? Task.CompletedTask
            : _database.SetSettingAsync(StateSetting, JsonSerializer.Serialize(state), cancellationToken);

    private static async Task<string> ProbeRateLimitTextAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        return await SimpleMonitorPassiveReadGate.RunAsync(async () =>
        {
            try
            {
                var task = (Task<JsonElement>)(EvaluateMethod.Invoke(
                    chrome,
                    new object[] { tab, RateLimitProbeExpression, cancellationToken, false })
                    ?? throw new InvalidOperationException("Rate-limit probe returned no task."));
                var value = await task.ConfigureAwait(false);
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private void ReleasePhysicalSendGate() => PhysicalSendGate.Release();

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining.TotalMinutes >= 1)
            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        return $"00:{Math.Max(0, remaining.Seconds):00}";
    }

    private static string Compact(string value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    internal sealed class SendPermit : IAsyncDisposable
    {
        private SimpleMonitorSafetyGate? _owner;

        internal SendPermit(SimpleMonitorSafetyGate owner, ChromeTab tab, ChatPageState state)
        {
            _owner = owner;
            Tab = tab;
            State = state;
        }

        internal ChromeTab Tab { get; }
        internal ChatPageState State { get; }

        internal Task RecordPhysicalAttemptAsync(CancellationToken cancellationToken)
            => _owner?.RecordPhysicalAttemptAsync(cancellationToken) ?? Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleasePhysicalSendGate();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record DurableState(
        DateTimeOffset? LastPhysicalAttemptUtc,
        DateTimeOffset? LastResponseCompletedUtc,
        bool RateLimitActive,
        int BackoffIndex,
        DateTimeOffset? RetryAtUtc,
        DateTimeOffset? DetectedAtUtc,
        string LastRateLimitText)
    {
        internal static DurableState Empty { get; } = new(null, null, false, 0, null, null, string.Empty);
    }
}

internal static class SimpleMonitorPassiveReadGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
