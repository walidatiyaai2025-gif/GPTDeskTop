from pathlib import Path

root = Path(__file__).resolve().parents[1]

def read(path: str) -> str:
    return (root / path).read_text(encoding='utf-8-sig')

def write(path: str, text: str) -> None:
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8', newline='\n')

def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    if old not in text:
        raise SystemExit(f'{label}: expected source block was not found')
    return text.replace(old, new, 1)

# -----------------------------------------------------------------------------
# Premium palette remains the one canonical visual language.
# -----------------------------------------------------------------------------
theme_path = 'src/GPTDeskTop/UI/FluentTheme.cs'
text = read(theme_path)
old_palette = '''    public static readonly Color Background = Color.FromArgb(245, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color SurfaceRaised = Color.FromArgb(252, 253, 255);
    public static readonly Color SurfaceHover = Color.FromArgb(241, 245, 249);
    public static readonly Color SurfacePressed = Color.FromArgb(226, 232, 240);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color AccentSubtle = Color.FromArgb(239, 246, 255);
    public static readonly Color AccentBorder = Color.FromArgb(147, 197, 253);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color MutedStrong = Color.FromArgb(71, 85, 105);
    public static readonly Color DisabledText = Color.FromArgb(148, 163, 184);
    public static readonly Color DisabledSurface = Color.FromArgb(241, 245, 249);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color FocusRing = Color.FromArgb(96, 165, 250);
    public static readonly Color Danger = Color.FromArgb(190, 24, 93);
    public static readonly Color DangerSubtle = Color.FromArgb(253, 242, 248);
    public static readonly Color Success = Color.FromArgb(5, 150, 105);
    public static readonly Color SuccessSubtle = Color.FromArgb(236, 253, 245);
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);
    public static readonly Color WarningSubtle = Color.FromArgb(255, 251, 235);
    public static readonly Color Info = Color.FromArgb(2, 132, 199);
    public static readonly Color InfoSubtle = Color.FromArgb(240, 249, 255);'''
new_palette = '''    // Premium dark runtime palette. The colors deliberately preserve the existing semantic
    // roles so every current screen and runtime state keeps the same behavior and meaning.
    public static readonly Color Background = Color.FromArgb(5, 14, 24);
    public static readonly Color Surface = Color.FromArgb(9, 23, 38);
    public static readonly Color SurfaceAlt = Color.FromArgb(12, 29, 47);
    public static readonly Color SurfaceRaised = Color.FromArgb(7, 20, 34);
    public static readonly Color SurfaceHover = Color.FromArgb(16, 40, 65);
    public static readonly Color SurfacePressed = Color.FromArgb(22, 53, 84);
    public static readonly Color Accent = Color.FromArgb(10, 113, 255);
    public static readonly Color AccentHover = Color.FromArgb(39, 130, 255);
    public static readonly Color AccentPressed = Color.FromArgb(0, 91, 214);
    public static readonly Color AccentSubtle = Color.FromArgb(11, 42, 74);
    public static readonly Color AccentBorder = Color.FromArgb(29, 104, 192);
    public static readonly Color Text = Color.FromArgb(235, 243, 255);
    public static readonly Color Muted = Color.FromArgb(135, 153, 179);
    public static readonly Color MutedStrong = Color.FromArgb(177, 194, 215);
    public static readonly Color DisabledText = Color.FromArgb(89, 108, 132);
    public static readonly Color DisabledSurface = Color.FromArgb(17, 31, 47);
    public static readonly Color Border = Color.FromArgb(28, 48, 70);
    public static readonly Color BorderStrong = Color.FromArgb(42, 67, 96);
    public static readonly Color FocusRing = Color.FromArgb(66, 153, 255);
    public static readonly Color Danger = Color.FromArgb(248, 81, 96);
    public static readonly Color DangerSubtle = Color.FromArgb(63, 25, 34);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color SuccessSubtle = Color.FromArgb(12, 52, 43);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color WarningSubtle = Color.FromArgb(60, 43, 15);
    public static readonly Color Info = Color.FromArgb(56, 189, 248);
    public static readonly Color InfoSubtle = Color.FromArgb(12, 44, 62);'''
if old_palette in text:
    text = text.replace(old_palette, new_palette)
elif new_palette not in text:
    raise SystemExit('FluentTheme palette block no longer matches expected source.')
text = text.replace('grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 254);',
                    'grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(11, 27, 44);')
text = text.replace('danger ? Color.FromArgb(252, 231, 243) : SurfacePressed',
                    'danger ? Color.FromArgb(82, 31, 42) : SurfacePressed')
text = text.replace('button.FlatAppearance.BorderColor = Color.FromArgb(249, 168, 212);',
                    'button.FlatAppearance.BorderColor = Color.FromArgb(121, 50, 62);')
text = text.replace('toolStrip.Padding = new Padding(4, 3, 4, 3);',
                    'toolStrip.Padding = new Padding(8, 5, 8, 5);')
write(theme_path, text)

# -----------------------------------------------------------------------------
# Canonical global FIFO send coordinator. This preserves the existing exactly-once
# snapshot semantics while moving physical composer authority to one app-wide queue.
# -----------------------------------------------------------------------------
outbound_source = r'''using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

public enum OutboundDeliveryPhase
{
    Queued,
    Sending,
    Accepted,
    ReconcileRequired,
    Completed
}

public sealed record OutboundDeliverySnapshot(
    long MonitorId,
    string ConversationKey,
    string MessageFingerprint,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc,
    string Reason);

public sealed record OutboundDeliveryStatus(
    long MonitorId,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc);

public sealed record OutboundQueueStatus(
    int QueuedCount,
    long? ActiveMonitorId,
    DateTimeOffset UpdatedUtc);

public sealed class OutboundDeliveryCoordinator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultInterSendGap = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<long, OutboundDeliverySnapshot> _snapshots = new();
    private readonly object _queueSync = new();
    private readonly Queue<QueueEntry> _queue = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _interSendGap;
    private QueueEntry? _active;
    private long _sequence;

    public OutboundDeliveryCoordinator(
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? interSendGap = null)
    {
        _delayAsync = delayAsync ?? Task.Delay;
        _interSendGap = interSendGap ?? DefaultInterSendGap;
    }

    public event Action<OutboundDeliveryStatus>? StatusChanged;
    public event Action<OutboundQueueStatus>? QueueStatusChanged;

    public int QueuedCount
    {
        get
        {
            lock (_queueSync)
                return _queue.Count(entry => !entry.Cancelled);
        }
    }

    public long? ActiveMonitorId
    {
        get { lock (_queueSync) return _active?.MonitorId; }
    }

    public string DisplayStatus
    {
        get
        {
            lock (_queueSync)
            {
                var queued = _queue.Count(entry => !entry.Cancelled);
                return _active is null
                    ? queued == 0 ? "IDLE" : $"WAITING {queued}"
                    : queued == 0 ? $"M{_active.MonitorId} SENDING" : $"M{_active.MonitorId} +{queued}";
            }
        }
    }

    public async Task<bool> SendOnceAsync(
        long monitorId,
        string conversationKey,
        string message,
        Func<Task<bool>> physicalSend,
        Action<string>? activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(physicalSend);
        using var flightScope = RuntimeFlightRecorder.BeginScope(monitorId, conversationKey);
        RuntimeFlightRecorder.Record("Delivery", "OperationRequested", reason: "global-serialized-logical-send");

        var fingerprint = Fingerprint(message);
        var logicalId = $"{monitorId}:{conversationKey}:{fingerprint}";
        using var queueLease = await AcquireQueueLeaseAsync(monitorId, logicalId, cancellationToken).ConfigureAwait(false);
        var physicalSendAttempted = false;
        try
        {
            await GlobalChatGptRateLimitCircuitBreaker.Shared.WaitUntilAllowedAsync(cancellationToken).ConfigureAwait(false);

            if (_snapshots.TryGetValue(monitorId, out var previous)
                && IsDuplicateInFlight(previous, conversationKey, fingerprint))
            {
                RuntimeFlightRecorder.Record("Delivery", "DuplicateSuppressed", "suppressed", "uncertain-or-in-flight");
                activity?.Invoke("Exactly-once guard: identical delivery is already in-flight or awaiting reconciliation; duplicate composer mutation suppressed.");
                return false;
            }

            var sending = new OutboundDeliverySnapshot(
                monitorId,
                conversationKey,
                fingerprint,
                OutboundDeliveryPhase.Sending,
                1,
                DateTimeOffset.UtcNow,
                "global-queue-authority-persisted-before-physical-send");
            SetSnapshot(sending);
            RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitRequested", "started", "global-queue-authority");

            bool accepted;
            try
            {
                physicalSendAttempted = true;
                accepted = await physicalSend().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetSnapshot(sending with
                {
                    Phase = OutboundDeliveryPhase.ReconcileRequired,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Reason = "physical-send-threw; observe-before-any-future-send"
                });
                RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitCompleted", "uncertain", ex.GetType().Name);
                throw;
            }

            SetSnapshot(sending with
            {
                Phase = accepted ? OutboundDeliveryPhase.Accepted : OutboundDeliveryPhase.ReconcileRequired,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Reason = accepted
                    ? "verified-user-message-receipt"
                    : "receipt-not-confirmed; no blind retry"
            });
            RuntimeFlightRecorder.Record(
                "Delivery",
                "PhysicalSubmitCompleted",
                accepted ? "confirmed" : "uncertain",
                accepted ? "verified-user-turn" : "receipt-not-confirmed");
            return accepted;
        }
        finally
        {
            if (physicalSendAttempted)
            {
                activity?.Invoke($"Global send queue: enforcing {_interSendGap.TotalSeconds:0}-second inter-send cooldown.");
                RuntimeFlightRecorder.Record("Delivery", "GlobalInterSendCooldown", "started", $"{_interSendGap.TotalSeconds:0}s", monitorId);
                try
                {
                    await _delayAsync(_interSendGap, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.InterSendCooldown");
                }
            }
        }
    }

    public IReadOnlyList<OutboundDeliverySnapshot> Snapshot()
        => _snapshots.Values.OrderBy(x => x.MonitorId).ToArray();

    public void MarkCompleted(long monitorId)
    {
        if (!_snapshots.TryGetValue(monitorId, out var state)
            || state.Phase is not (OutboundDeliveryPhase.Accepted or OutboundDeliveryPhase.ReconcileRequired))
            return;

        var reason = state.Phase == OutboundDeliveryPhase.ReconcileRequired
            ? "response-observed-after-uncertain-send"
            : "response-observed";
        SetSnapshot(state with
        {
            Phase = OutboundDeliveryPhase.Completed,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Reason = reason
        });
        RuntimeFlightRecorder.Record("Delivery", "OperationCompleted", "completed", reason, monitorId);
    }

    public void CancelMonitor(long monitorId)
    {
        lock (_queueSync)
        {
            foreach (var entry in _queue.Where(entry => entry.MonitorId == monitorId))
                entry.Cancel();
            PruneCancelledHeadLocked();
            PublishQueueStatusLocked();
        }
    }

    private async Task<QueueLease> AcquireQueueLeaseAsync(long monitorId, string logicalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QueueEntry entry;
        lock (_queueSync)
        {
            entry = new QueueEntry(++_sequence, monitorId, logicalId, cancellationToken);
            _queue.Enqueue(entry);
            entry.CancellationRegistration = cancellationToken.Register(() => CancelQueuedEntry(entry));
            RuntimeFlightRecorder.Record("Delivery", "GlobalQueueEnqueued", "queued", $"sequence={entry.Sequence}", monitorId);
            GrantNextLocked();
            PublishQueueStatusLocked();
        }

        try
        {
            return await entry.Completion.Task.ConfigureAwait(false);
        }
        catch
        {
            entry.CancellationRegistration.Dispose();
            throw;
        }
    }

    private void CancelQueuedEntry(QueueEntry entry)
    {
        lock (_queueSync)
        {
            if (ReferenceEquals(_active, entry))
                return;
            entry.Cancel();
            PruneCancelledHeadLocked();
            GrantNextLocked();
            PublishQueueStatusLocked();
        }
    }

    private void Release(QueueEntry entry)
    {
        lock (_queueSync)
        {
            if (!ReferenceEquals(_active, entry))
                return;
            _active = null;
            entry.CancellationRegistration.Dispose();
            RuntimeFlightRecorder.Record("Delivery", "GlobalQueueReleased", "released", $"sequence={entry.Sequence}", entry.MonitorId);
            PruneCancelledHeadLocked();
            GrantNextLocked();
            PublishQueueStatusLocked();
        }
    }

    private void GrantNextLocked()
    {
        if (_active is not null)
            return;

        PruneCancelledHeadLocked();
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (next.Cancelled)
                continue;
            _active = next;
            var lease = new QueueLease(this, next);
            if (next.Completion.TrySetResult(lease))
            {
                RuntimeFlightRecorder.Record("Delivery", "GlobalQueueAuthorityGranted", "granted", $"sequence={next.Sequence}", next.MonitorId);
                break;
            }
            _active = null;
        }
    }

    private void PruneCancelledHeadLocked()
    {
        while (_queue.Count > 0 && _queue.Peek().Cancelled)
            _queue.Dequeue();
    }

    private void PublishQueueStatusLocked()
    {
        var handlers = QueueStatusChanged;
        if (handlers is null)
            return;
        var status = new OutboundQueueStatus(
            _queue.Count(entry => !entry.Cancelled),
            _active?.MonitorId,
            DateTimeOffset.UtcNow);
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<OutboundQueueStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.QueueStatusChanged"); }
        }
    }

    private void SetSnapshot(OutboundDeliverySnapshot snapshot)
    {
        _snapshots[snapshot.MonitorId] = snapshot;
        PublishStatus(new OutboundDeliveryStatus(
            snapshot.MonitorId,
            snapshot.Phase,
            snapshot.PhysicalSendCount,
            snapshot.UpdatedUtc));
    }

    private void PublishStatus(OutboundDeliveryStatus status)
    {
        var handlers = StatusChanged;
        if (handlers is null)
            return;
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<OutboundDeliveryStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.StatusChanged"); }
        }
    }

    private static bool IsDuplicateInFlight(OutboundDeliverySnapshot previous, string conversationKey, string fingerprint)
        => previous.ConversationKey == conversationKey
           && previous.MessageFingerprint == fingerprint
           && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired
           && DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow;

    public static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private sealed class QueueEntry
    {
        public QueueEntry(long sequence, long monitorId, string logicalId, CancellationToken cancellationToken)
        {
            Sequence = sequence;
            MonitorId = monitorId;
            LogicalId = logicalId;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<QueueLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public long Sequence { get; }
        public long MonitorId { get; }
        public string LogicalId { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<QueueLease> Completion { get; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool Cancelled { get; private set; }

        public void Cancel()
        {
            if (Cancelled) return;
            Cancelled = true;
            Completion.TrySetCanceled(CancellationToken);
        }
    }

    public sealed class QueueLease : IDisposable
    {
        private readonly OutboundDeliveryCoordinator _owner;
        private readonly QueueEntry _entry;
        private int _disposed;

        private QueueLease(OutboundDeliveryCoordinator owner, QueueEntry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.Release(_entry);
        }
    }
}
'''
write('src/GPTDeskTop/Runtime/OutboundDeliveryCoordinator.cs', outbound_source)

# -----------------------------------------------------------------------------
# One application-wide rate-limit circuit breaker. Visible modal detection is fed from
# ChromeDevToolsService; cooldown is 5/10/15/30 minutes and only the first observation
# at a due boundary becomes the global probe decision.
# -----------------------------------------------------------------------------
breaker_source = r'''using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

public sealed record GlobalRateLimitStatus(
    bool IsActive,
    int BackoffStep,
    DateTimeOffset? RetryAtUtc,
    TimeSpan Remaining,
    string EventName,
    string Detail);

public sealed class GlobalChatGptRateLimitCircuitBreaker
{
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    public static GlobalChatGptRateLimitCircuitBreaker Shared { get; } = new();

    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private bool _active;
    private int _backoffIndex;
    private DateTimeOffset _retryAtUtc;

    public GlobalChatGptRateLimitCircuitBreaker(
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public event Action<GlobalRateLimitStatus>? StatusChanged;

    public bool IsActive
    {
        get { lock (_sync) return _active; }
    }

    public int BackoffStep
    {
        get { lock (_sync) return _active ? _backoffIndex + 1 : 0; }
    }

    public DateTimeOffset? RetryAtUtc
    {
        get { lock (_sync) return _active ? _retryAtUtc : null; }
    }

    public string DisplayStatus
    {
        get
        {
            lock (_sync)
            {
                if (!_active) return "READY";
                var remaining = _retryAtUtc - _utcNow();
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                return $"PAUSED {remaining:hh\\:mm\\:ss}";
            }
        }
    }

    public void ObserveVisibleState(string? visibleGlobalRateLimitText)
    {
        var limited = IsRateLimitText(visibleGlobalRateLimitText);
        GlobalRateLimitStatus? publish = null;
        string? secondEvent = null;

        lock (_sync)
        {
            var now = _utcNow();
            if (!_active)
            {
                if (!limited) return;
                _active = true;
                _backoffIndex = 0;
                _retryAtUtc = now + BackoffSchedule[_backoffIndex];
                publish = SnapshotLocked("RateLimitDetected", "visible-global-chatgpt-rate-limit");
                secondEvent = "GlobalSendPause";
            }
            else
            {
                if (now < _retryAtUtc)
                    return;

                RuntimeFlightRecorder.Record("RateLimit", "RateLimitProbe", "started", "single-global-cooldown-probe");
                if (limited)
                {
                    _backoffIndex = Math.Min(_backoffIndex + 1, BackoffSchedule.Length - 1);
                    _retryAtUtc = now + BackoffSchedule[_backoffIndex];
                    publish = SnapshotLocked("RateLimitStillActive", $"backoff={BackoffSchedule[_backoffIndex].TotalMinutes:0}m");
                }
                else
                {
                    _active = false;
                    _backoffIndex = 0;
                    _retryAtUtc = default;
                    publish = SnapshotLocked("RateLimitCleared", "visible-global-rate-limit-absent-at-probe");
                    secondEvent = "GlobalSendResume";
                }
            }
        }

        if (publish is not null)
        {
            RuntimeFlightRecorder.Record("RateLimit", publish.EventName, publish.IsActive ? "paused" : "ready", publish.Detail);
            Publish(publish);
        }
        if (secondEvent is not null)
            RuntimeFlightRecorder.Record("RateLimit", secondEvent, secondEvent == "GlobalSendPause" ? "paused" : "resumed", "global-send-authority");
    }

    public async Task WaitUntilAllowedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_sync)
            {
                if (!_active) return;
                wait = _retryAtUtc - _utcNow();
                if (wait <= TimeSpan.Zero) wait = TimeSpan.FromMilliseconds(250);
                if (wait > TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            }
            await _delayAsync(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool IsRateLimitText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("making requests too quickly", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("temporarily limited access", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("temporarily limited access to your conversations", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("please wait a few minutes before trying again", StringComparison.OrdinalIgnoreCase);
    }

    private GlobalRateLimitStatus SnapshotLocked(string eventName, string detail)
    {
        var remaining = _active ? _retryAtUtc - _utcNow() : TimeSpan.Zero;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return new GlobalRateLimitStatus(
            _active,
            _active ? _backoffIndex + 1 : 0,
            _active ? _retryAtUtc : null,
            remaining,
            eventName,
            detail);
    }

    private void Publish(GlobalRateLimitStatus status)
    {
        var handlers = StatusChanged;
        if (handlers is null) return;
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<GlobalRateLimitStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "GlobalChatGptRateLimitCircuitBreaker.StatusChanged"); }
        }
    }
}
'''
write('src/GPTDeskTop/Runtime/GlobalChatGptRateLimitCircuitBreaker.cs', breaker_source)

# -----------------------------------------------------------------------------
# Chat page state gains a dedicated global-modal field; old four-argument construction
# remains source-compatible via the default value.
# -----------------------------------------------------------------------------
models_path = 'src/GPTDeskTop/Models/Models.cs'
models = read(models_path)
models = replace_once(
    models,
    '''public sealed record ChatPageState(
    int AssistantCount,
    string LastAssistantText,
    bool IsGenerating,
    string ErrorText);''',
    '''public sealed record ChatPageState(
    int AssistantCount,
    string LastAssistantText,
    bool IsGenerating,
    string ErrorText,
    string GlobalRateLimitText = "");''',
    'ChatPageState global rate-limit field')
write(models_path, models)

# -----------------------------------------------------------------------------
# Visible global rate-limit detection. It intentionally excludes message-turn DOM so old
# conversation text cannot become breaker authority.
# -----------------------------------------------------------------------------
chrome_path = 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
chrome = read(chrome_path)
chrome = chrome.replace('window.__gptDesktopChatStateCache?.version === 7', 'window.__gptDesktopChatStateCache?.version === 8')
chrome = chrome.replace('  const version = 7;', '  const version = 8;')
rate_detector = r'''  const globalRateLimitPattern = /too many requests|making requests too quickly|temporarily limited access(?: to your conversations)?|please wait a few minutes before trying again/i;
  const findGlobalRateLimitText = () => {
    const selectors = ['[role="dialog"]', '[aria-modal="true"]', '[role="alert"]'];
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (!visible(element)) continue;
        if (element.closest('[data-message-author-role]')) continue;
        const text = (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim();
        if (text && text.length <= 2500 && globalRateLimitPattern.test(text)) return text;
      }
    }
    return '';
  };
'''
anchor = '  const errorPattern = /message delivery timed out|something went wrong|there was an error|network error|failed to (generate|load)|unable to (generate|load)|error generating|حدث خطأ|خطأ في الشبكة|تعذر/i;\n'
if 'const globalRateLimitPattern' not in chrome:
    if anchor not in chrome:
        raise SystemExit('Chrome global-rate-limit detector anchor missing')
    chrome = chrome.replace(anchor, anchor + rate_detector, 1)
chrome = chrome.replace(
    "snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '', autoFollow:",
    "snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '', globalRateLimitText: '', autoFollow:")
read_anchor = "    const messages = document.querySelectorAll('[data-message-author-role=\"assistant\"]');"
if 'const globalRateLimitText = findGlobalRateLimitText();' not in chrome:
    chrome = replace_once(chrome, read_anchor,
        "    const globalRateLimitText = findGlobalRateLimitText();\n" + read_anchor,
        'Chrome state global-rate-limit read')
chrome = chrome.replace(
    "state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText, autoFollow:",
    "state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText, globalRateLimitText, autoFollow:")
constructor_old = '''        return new ChatPageState(
            value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0,
            value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(),
            value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty);'''
constructor_new = '''        return new ChatPageState(
            value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0,
            value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(),
            value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("globalRateLimitText", out var rateLimit) ? rateLimit.GetString() ?? string.Empty : string.Empty);'''
chrome = replace_once(chrome, constructor_old, constructor_new, 'Chrome ChatPageState construction')
write(chrome_path, chrome)

# -----------------------------------------------------------------------------
# Wire every monitor to the shared breaker. All automated send paths already converge on
# SendWhenReadyAsync; fresh-chat recovery is additionally stopped before creation whenever
# another monitor has activated the global breaker.
# -----------------------------------------------------------------------------
monitor_path = 'src/GPTDeskTop/Services/ChatGptMonitorService.cs'
monitor = read(monitor_path)
monitor = replace_once(
    monitor,
    '    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();\n',
    '    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();\n    private readonly GlobalChatGptRateLimitCircuitBreaker _globalRateLimit = GlobalChatGptRateLimitCircuitBreaker.Shared;\n',
    'monitor global breaker field')
monitor = replace_once(
    monitor,
    '    public bool IsRunning { get { lock (_sync) return _running.Count > 0; } }\n',
    '''    public bool IsRunning { get { lock (_sync) return _running.Count > 0; } }
    public string GlobalSendQueueStatus => _outboundDelivery.DisplayStatus;
    public string GlobalRateLimitStatus => _globalRateLimit.DisplayStatus;
    public bool IsPausedByGlobalRateLimit => _globalRateLimit.IsActive;
''',
    'monitor global status properties')
constructor_old = '    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)\n    { _chrome = chrome; _database = database; _config = config; }'
constructor_new = '''    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;
        _outboundDelivery.QueueStatusChanged += status =>
        {
            Activity?.Invoke(status.ActiveMonitorId ?? 0, $"Global Send Queue: {_outboundDelivery.DisplayStatus}");
            RunningStateChanged?.Invoke();
        };
        _globalRateLimit.StatusChanged += status =>
        {
            Activity?.Invoke(0, status.IsActive
                ? $"CHATGPT RATE LIMITED — ALL AUTOMATED SENDS PAUSED — {_globalRateLimit.DisplayStatus}"
                : "ChatGPT global rate limit cleared — serialized sends resumed.");
            RunningStateChanged?.Invoke();
        };
    }'''
monitor = replace_once(monitor, constructor_old, constructor_new, 'monitor constructor safety wiring')
monitor = replace_once(
    monitor,
    '        runtime.Cancellation.Cancel();\n',
    '        _outboundDelivery.CancelMonitor(monitorId);\n        runtime.Cancellation.Cancel();\n',
    'monitor stop queue cancellation')
state_anchor = '''                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                    var text = GetEffectiveResponse(state);'''
state_replacement = '''                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                    _globalRateLimit.ObserveVisibleState(state.GlobalRateLimitText);
                    if (_globalRateLimit.IsActive)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} PausedByGlobalRateLimit — {_globalRateLimit.DisplayStatus}");
                        continue;
                    }
                    var text = GetEffectiveResponse(state);'''
monitor = replace_once(monitor, state_anchor, state_replacement, 'monitor loop rate-limit guard')
monitor = replace_once(
    monitor,
    '''            var initialText = GetEffectiveResponse(initial);''',
    '''            _globalRateLimit.ObserveVisibleState(initial.GlobalRateLimitText);
            var initialText = GetEffectiveResponse(initial);''',
    'initial rate-limit observation')
monitor = replace_once(
    monitor,
    '''            await ApplyModelRouteAsync(monitor, tab, recovery: false, contextRotation: false, cancellationToken);''',
    '''            if (!_globalRateLimit.IsActive)
                await ApplyModelRouteAsync(monitor, tab, recovery: false, contextRotation: false, cancellationToken);''',
    'initial model route breaker guard')
monitor = replace_once(
    monitor,
    '''    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)
    {
        var prefix = $"[{monitor.Title}]";''',
    '''    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)
    {
        if (_globalRateLimit.IsActive)
        {
            Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Message-count rotation deferred by global ChatGPT rate limit.");
            return null;
        }
        var prefix = $"[{monitor.Title}]";''',
    'message-count rotation breaker guard')
monitor = monitor.replace(
    '        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);',
    '''        if (_globalRateLimit.IsActive)
            return null;
        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);''',
    1)
# The three inline recovery/context paths use the exact old-tab/new-tab sequence.
inline_create = 'var oldTab = tab; var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);'
inline_guard = 'var oldTab = tab; if (_globalRateLimit.IsActive) { Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat recovery deferred by global ChatGPT rate limit."); continue; } var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);'
monitor = monitor.replace(inline_create, inline_guard)
write(monitor_path, monitor)

# -----------------------------------------------------------------------------
# Real multiline editor geometry: source properties + actual 96px rows.
# -----------------------------------------------------------------------------
for path in ['src/GPTDeskTop/UI/MonitorSettingsForm.cs', 'src/GPTDeskTop/UI/SettingsForm.cs']:
    source = read(path)
    source = source.replace('ScrollBars = ScrollBars.Vertical, WordWrap = true }',
                            'ScrollBars = ScrollBars.Vertical, WordWrap = true, AutoSize = false }')
    source = source.replace('ScrollBars = ScrollBars.Vertical, WordWrap = true, Text =',
                            'ScrollBars = ScrollBars.Vertical, WordWrap = true, AutoSize = false, Text =')
    write(path, source)

# -----------------------------------------------------------------------------
# MainForm owns the visible safety state. A timer updates status text only; it never rebuilds
# controls. Add queue/rate chips to the existing single header.
# -----------------------------------------------------------------------------
main_path = 'src/GPTDeskTop/UI/MainForm.cs'
main = read(main_path)
main = main.replace('MinimumSize = new Size(1280, 760);', 'MinimumSize = new Size(980, 680);')
main = main.replace('Padding = new Padding(16),\n            BackColor = FluentTheme.Background',
                    'Padding = new Padding(10),\n            BackColor = FluentTheme.Background', 1)
main = main.replace('root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));',
                    'root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));', 1)
main = replace_once(
    main,
    '    private readonly Label _runningMetricValue = CreateMetricValue("0");\n',
    '''    private readonly Label _runningMetricValue = CreateMetricValue("0");
    private readonly Label _sendQueueMetricValue = CreateMetricValue("IDLE");
    private readonly Label _rateLimitMetricValue = CreateMetricValue("READY");
    private readonly System.Windows.Forms.Timer _safetyStatusTimer = new() { Interval = 1000 };
''',
    'MainForm safety metric fields')
main = replace_once(
    main,
    '''        WireEvents();
        ConfigureTooltips();''',
    '''        WireEvents();
        _safetyStatusTimer.Tick += (_, _) => UpdateGlobalSafetyMetrics();
        FormClosed += (_, _) => { _safetyStatusTimer.Stop(); _safetyStatusTimer.Dispose(); };
        _safetyStatusTimer.Start();
        UpdateGlobalSafetyMetrics();
        ConfigureTooltips();''',
    'MainForm safety timer wiring')
main = replace_once(
    main,
    '''        metrics.Controls.Add(CreateMetricChip("Running", _runningMetricValue));''',
    '''        metrics.Controls.Add(CreateMetricChip("Rate Limit", _rateLimitMetricValue));
        metrics.Controls.Add(CreateMetricChip("Global Send", _sendQueueMetricValue));
        metrics.Controls.Add(CreateMetricChip("Running", _runningMetricValue));''',
    'MainForm safety chips')
activity_anchor = '''    private void OnMonitorActivity(long id, string message)
        => Ui(() => AppendActivity($"M{id}: {message}"));'''
activity_replacement = '''    private void OnMonitorActivity(long id, string message)
        => Ui(() =>
        {
            UpdateGlobalSafetyMetrics();
            AppendActivity(id > 0 ? $"M{id}: {message}" : message);
        });

    private void UpdateGlobalSafetyMetrics()
    {
        _sendQueueMetricValue.Text = _monitor.GlobalSendQueueStatus;
        _rateLimitMetricValue.Text = _monitor.GlobalRateLimitStatus;
        _rateLimitMetricValue.ForeColor = _monitor.IsPausedByGlobalRateLimit ? FluentTheme.Danger : FluentTheme.Success;
        var baseTitle = $"GPTDeskTop v{GetAppVersion()}";
        Text = _monitor.IsPausedByGlobalRateLimit
            ? $"{baseTitle} — CHATGPT RATE LIMITED — ALL AUTOMATED SENDS PAUSED"
            : baseTitle;
    }'''
main = replace_once(main, activity_anchor, activity_replacement, 'MainForm safety metric updater')
write(main_path, main)

# -----------------------------------------------------------------------------
# Remove the second GPTDeskTop brand/header from the left rail. The rail remains navigation;
# MainForm owns the one canonical header. Also collapse auxiliary top-level runtime surfaces
# after their command buttons are harvested so they cannot overlap the dashboard.
# -----------------------------------------------------------------------------
shell_path = 'src/GPTDeskTop/UI/PremiumRuntimeShellExperience.cs'
shell = read(shell_path)
shell = shell.replace('control.Parent?.ScrollControlIntoView(control);',
                      '(control.Parent as ScrollableControl)?.ScrollControlIntoView(control);')
shell = shell.replace('FocusControl(grid ?? main);', 'FocusControl(grid is null ? main : grid);')
shell = shell.replace('layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));',
                      'layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));')
shell = shell.replace('        layout.Controls.Add(BuildBrand(), 0, 0);\n', '')
shell = shell.replace('main.MinimumSize = new Size(Math.Max(main.MinimumSize.Width, 1280), Math.Max(main.MinimumSize.Height, 760));',
                      'main.MinimumSize = new Size(Math.Max(main.MinimumSize.Width, 1100), Math.Max(main.MinimumSize.Height, 680));')
shell = shell.replace('if (main.Width < 1280 || main.Height < 760)\n            main.Size = new Size(Math.Max(main.Width, 1280), Math.Max(main.Height, 760));',
                      'if (main.Width < 1100 || main.Height < 680)\n            main.Size = new Size(Math.Max(main.Width, 1100), Math.Max(main.Height, 680));')
write(shell_path, shell)

workspace_path = 'src/GPTDeskTop/UI/OperatorWorkspaceV2Experience.cs'
workspace = read(workspace_path)
workspace = replace_once(
    workspace,
    '''            development.Visible = false;
            development.Height = 0;
            development.MinimumSize = Size.Empty;''',
    '''            development.Visible = false;
            development.Height = 0;
            development.MinimumSize = Size.Empty;
            runtimeHealth.Visible = false;
            runtimeHealth.Height = 0;
            runtimeHealth.MinimumSize = Size.Empty;''',
    'single-surface auxiliary control collapse')
write(workspace_path, workspace)

# -----------------------------------------------------------------------------
# Deterministic unit coverage for FIFO serialization, five-second gap, failure/cancellation,
# duplicate suppression, and the global 5/10/15/30 rate-limit state machine.
# -----------------------------------------------------------------------------
tests_source = r'''using System.Collections.Concurrent;
using GPTDeskTop.Runtime;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalOutboundSafetyRegressionTests
{
    [Fact]
    public async Task ThreeMonitors_AreSerializedInFifoOrder_WithFiveSecondGlobalGap()
    {
        var delays = new ConcurrentQueue<TimeSpan>();
        var coordinator = new OutboundDeliveryCoordinator(
            (delay, _) => { delays.Enqueue(delay); return Task.CompletedTask; });
        var order = new ConcurrentQueue<long>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.SendOnceAsync(1, "c1", "a", async () =>
        {
            order.Enqueue(1);
            firstStarted.TrySetResult();
            await releaseFirst.Task;
            return true;
        }, null, CancellationToken.None);
        await firstStarted.Task;

        var second = coordinator.SendOnceAsync(2, "c2", "b", () =>
        {
            order.Enqueue(2);
            return Task.FromResult(true);
        }, null, CancellationToken.None);
        await Task.Yield();
        var third = coordinator.SendOnceAsync(3, "c3", "c", () =>
        {
            order.Enqueue(3);
            return Task.FromResult(true);
        }, null, CancellationToken.None);

        Assert.Equal(2, coordinator.QueuedCount);
        releaseFirst.TrySetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.True(await third);
        Assert.Equal(new long[] { 1, 2, 3 }, order.ToArray());
        Assert.Equal(3, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(5), delay));
    }

    [Fact]
    public async Task PhysicalSendAuthority_IsNeverOwnedByTwoMonitorsAtOnce()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var active = 0;
        var maxActive = 0;
        async Task<bool> Send()
        {
            var now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return true;
        }

        await Task.WhenAll(
            coordinator.SendOnceAsync(11, "a", "one", Send, null, CancellationToken.None),
            coordinator.SendOnceAsync(12, "b", "two", Send, null, CancellationToken.None));
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task FailedMonitor_DoesNotDeadlockNextMonitor()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var failed = coordinator.SendOnceAsync(21, "a", "one",
            () => throw new IOException("simulated"), null, CancellationToken.None);
        var next = coordinator.SendOnceAsync(22, "b", "two",
            () => Task.FromResult(true), null, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => failed);
        Assert.True(await next);
    }

    [Fact]
    public async Task CancelledQueuedMonitor_IsRemovedWithoutBlockingFollower()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.SendOnceAsync(31, "a", "one", async () =>
        {
            await release.Task;
            return true;
        }, null, CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        var second = coordinator.SendOnceAsync(32, "b", "two", () => Task.FromResult(true), null, cancelled.Token);
        var third = coordinator.SendOnceAsync(33, "c", "three", () => Task.FromResult(true), null, CancellationToken.None);
        cancelled.Cancel();
        release.TrySetResult();
        Assert.True(await first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.True(await third);
    }

    [Fact]
    public async Task UncertainIdenticalSend_IsNotPhysicallyClickedTwice()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var physical = 0;
        Assert.False(await coordinator.SendOnceAsync(41, "same", "continue", () =>
        {
            Interlocked.Increment(ref physical);
            return Task.FromResult(false);
        }, null, CancellationToken.None));
        Assert.False(await coordinator.SendOnceAsync(41, "same", "continue", () =>
        {
            Interlocked.Increment(ref physical);
            return Task.FromResult(true);
        }, null, CancellationToken.None));
        Assert.Equal(1, physical);
    }

    [Theory]
    [InlineData("Too many requests")]
    [InlineData("You are making requests too quickly")]
    [InlineData("We have temporarily limited access to your conversations")]
    [InlineData("Please wait a few minutes before trying again")]
    public void RateLimitMarkers_AreDetectedCaseInsensitively(string text)
        => Assert.True(GlobalChatGptRateLimitCircuitBreaker.IsRateLimitText(text.ToUpperInvariant()));

    [Fact]
    public void GlobalRateLimit_BackoffProgressesFiveTenFifteenThirty_ThenClears()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);

        breaker.ObserveVisibleState("Too many requests");
        Assert.True(breaker.IsActive);
        Assert.Equal(1, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(5), breaker.RetryAtUtc);

        now = now.AddMinutes(5);
        breaker.ObserveVisibleState("temporarily limited access");
        Assert.Equal(2, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(10), breaker.RetryAtUtc);

        now = now.AddMinutes(10);
        breaker.ObserveVisibleState("making requests too quickly");
        Assert.Equal(3, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(15), breaker.RetryAtUtc);

        now = now.AddMinutes(15);
        breaker.ObserveVisibleState("Too many requests");
        Assert.Equal(4, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(30), breaker.RetryAtUtc);

        now = now.AddMinutes(30);
        breaker.ObserveVisibleState(string.Empty);
        Assert.False(breaker.IsActive);
        Assert.Equal(0, breaker.BackoffStep);
        Assert.Null(breaker.RetryAtUtc);
    }

    [Fact]
    public void ClearingBeforeCooldown_DoesNotPrematurelyResume()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);
        breaker.ObserveVisibleState("Too many requests");
        now = now.AddMinutes(4);
        breaker.ObserveVisibleState(string.Empty);
        Assert.True(breaker.IsActive);
        now = now.AddMinutes(1);
        breaker.ObserveVisibleState(string.Empty);
        Assert.False(breaker.IsActive);
    }
}
'''
write('tests/GPTDeskTop.RuntimeTests/GlobalOutboundSafetyRegressionTests.cs', tests_source)

print('Premium UI + final global runtime safety closure applied.')
