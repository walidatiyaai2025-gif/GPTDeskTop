using System.Reflection;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record SimpleMonitorInspectorSnapshot(
    string State,
    int CurrentMessage,
    int TotalMessages,
    int SentMessages,
    int PendingMessages,
    int PassiveReadRetries,
    string LastCdpEvent,
    string LastError);

public sealed class SimpleMonitorRunner : IAsyncDisposable
{
    private static readonly MethodInfo PassiveStateReader = typeof(ChromeDevToolsService).GetMethod(
        "ReadChatStateCoreAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(ChromeDevToolsService).FullName, "ReadChatStateCoreAsync");

    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private int _passiveReadRetries;
    private int _sentMessages;
    private int _pendingMessages;
    private int _currentMessage;
    private int _totalMessages;
    private string _lastCdpEvent = "Idle";
    private string _lastError = string.Empty;
    private readonly SimpleMonitorSafetyGate _safety;

    public SimpleMonitorRunner() : this(null) { }

    public SimpleMonitorRunner(LocalDatabase? database)
    {
        _safety = new SimpleMonitorSafetyGate(database);
    }

    public event Action<string>? StatusChanged;
    public event Action<int, int, string>? MessageChanged;
    public event Action<int, int, string>? MessageSent;
    public event Action<SimpleMonitorInspectorSnapshot>? InspectorChanged;
    public bool IsRunning { get { lock (_sync) return _worker is { IsCompleted: false }; } }

    public Task StartAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        IReadOnlyList<string> messages,
        int delaySeconds,
        CancellationToken cancellationToken = default)
    {
        if (messages is null)
            throw new ArgumentNullException(nameof(messages));

        var steps = messages.Select(message => new SimpleMonitorMessageStep
        {
            Text = message,
            Enabled = true,
            Sent = false
        }).ToArray();

        return StartAsync(
            session,
            conversationUrl,
            steps,
            delaySeconds,
            loop: true,
            checkpoint: null,
            cancellationToken);
    }

    public Task StartAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        IReadOnlyList<SimpleMonitorMessageStep> messages,
        int defaultDelaySeconds,
        bool loop,
        CancellationToken cancellationToken = default)
        => StartAsync(session, conversationUrl, messages, defaultDelaySeconds, loop, checkpoint: null, cancellationToken);

    public Task StartAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        IReadOnlyList<SimpleMonitorMessageStep> messages,
        int defaultDelaySeconds,
        bool loop,
        Func<int, int, string, CancellationToken, Task>? checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!SimpleMonitorProfileSession.TryGetConversationId(conversationUrl, out _))
            throw new ArgumentException("A stable ChatGPT /c/{conversation-id} URL is required.", nameof(conversationUrl));
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("At least one stored message is required.", nameof(messages));

        var normalizedDefaultDelay = Math.Clamp(defaultDelaySeconds, 15, 3600);
        var runtimeMessages = messages
            .Select((message, index) => new { message, index })
            .Where(item => item.message is not null && item.message.Enabled && (loop || !item.message.Sent))
            .Select(item => new RuntimeMessage(
                item.index,
                new SimpleMonitorMessageStep
                {
                    Label = item.message.Label,
                    Text = item.message.Text,
                    Enabled = true,
                    DelaySeconds = item.message.DelaySeconds,
                    Sent = item.message.Sent
                }))
            .ToArray();

        if (runtimeMessages.Any(message => string.IsNullOrWhiteSpace(message.Step.Text)))
            throw new ArgumentException("Every enabled stored message must contain text.", nameof(messages));

        lock (_sync)
        {
            if (_worker is { IsCompleted: false })
                throw new InvalidOperationException("The monitor-only runtime is already running.");

            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellation.Token;
            _passiveReadRetries = 0;
            _sentMessages = messages.Count(message => message is not null && message.Enabled && message.Sent);
            _pendingMessages = runtimeMessages.Length;
            _currentMessage = 0;
            _totalMessages = messages.Count;
            _lastCdpEvent = "Starting";
            _lastError = string.Empty;

            if (runtimeMessages.Length == 0)
            {
                _worker = Task.Run(() =>
                {
                    StatusChanged?.Invoke("Plan complete. All enabled RUN ONCE messages are already checkpointed; nothing will be resent.");
                    PublishInspector("Complete");
                }, token);
            }
            else
            {
                _worker = Task.Run(
                    () => RunLoopAsync(session, conversationUrl, runtimeMessages, normalizedDefaultDelay, loop, checkpoint, token),
                    token);
            }
        }
        PublishInspector("Starting");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        lock (_sync)
        {
            cancellation = _cancellation;
            worker = _worker;
        }

        if (cancellation is null || worker is null) return;
        cancellation.Cancel();
        try { await worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_worker, worker)) _worker = null;
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task RunLoopAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        IReadOnlyList<RuntimeMessage> messages,
        int defaultDelaySeconds,
        bool loop,
        Func<int, int, string, CancellationToken, Task>? checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            SetStatus("Connecting to the selected Chrome profile...", "Connecting");
            await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var tab = await session.ResolveConversationAsync(conversationUrl, openIfMissing: true, cancellationToken).ConfigureAwait(false)
                ?? throw new SimpleMonitorBlockedException("The selected ChatGPT conversation could not be opened in this profile.");

            var initial = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);
            ThrowIfUnsafe(initial);
            if (initial.IsGenerating)
            {
                SetStatus("The selected chat is already generating. Waiting for it to finish...", "WaitingExistingResponse");
                await WaitForExistingGenerationAsync(session, conversationUrl, defaultDelaySeconds, cancellationToken).ConfigureAwait(false);
            }

            var messageIndex = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
                var before = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);
                ThrowIfUnsafe(before);
                if (before.IsGenerating)
                {
                    SetStatus("ChatGPT is generating. Send is blocked until the response finishes.", "WaitingResponse");
                    await WaitForExistingGenerationAsync(session, conversationUrl, defaultDelaySeconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await using var sendPermit = await _safety.AcquireSendPermitAsync(
                    session.Chrome,
                    token => RequireSameConversationAsync(session, conversationUrl, token),
                    (liveTab, token) => ReadPassiveStateResilientAsync(session.Chrome, liveTab, token),
                    status => SetStatus(status, "SendGate"),
                    cancellationToken).ConfigureAwait(false);
                tab = sendPermit.Tab;
                before = sendPermit.State;
                    ThrowIfUnsafe(before);

                var runtimeMessage = messages[messageIndex];
                var step = runtimeMessage.Step;
                var message = step.Text;
                _currentMessage = runtimeMessage.OriginalIndex + 1;
                MessageChanged?.Invoke(_currentMessage, _totalMessages, message);
                SetStatus($"Sending stored message {_currentMessage}/{_totalMessages} to the same chat...", "Sending");
                _lastCdpEvent = "SendChatMessageVerifiedAsync";
                PublishInspector("Sending");

                await sendPermit.RecordPhysicalAttemptAsync(CancellationToken.None).ConfigureAwait(false);
                bool sent;
                try
                {
                    sent = await SimpleMonitorPassiveReadGate.RunAsync(
                        () => session.Chrome.SendChatMessageVerifiedAsync(
                            tab,
                            message,
                            cancellationToken,
                            requireNewTurn: true),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var rateLimited = false;
                    try
                    {
                        rateLimited = await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // The send outcome is already uncertain. A failed diagnostic probe must never
                        // convert uncertainty into permission to try the composer again.
                    }

                    if (rateLimited)
                    {
                        SetStatus("RATE LIMITED — physical send outcome is uncertain. Breaker is active and automatic retry is blocked to prevent duplicate delivery.", "RateLimited");
                        throw new SimpleMonitorBlockedException(
                            "ChatGPT rate limited the profile while the physical send outcome was uncertain. Automatic retry is blocked to prevent a duplicate. Wait for the cooldown, inspect the same chat, then press Start only after reconciling whether the message arrived.");
                    }

                    throw new SimpleMonitorBlockedException(
                        $"The physical send outcome is uncertain ({ex.Message}). Automatic retry is blocked to prevent duplicate delivery. Inspect the same chat before pressing Start again.");
                }

                if (!sent)
                {
                    if (await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false))
                    {
                        SetStatus("RATE LIMITED — physical submit was rejected. Safe backoff completed; retry remains behind the global send gate.", "RateLimited");
                        continue;
                    }
                    throw new SimpleMonitorBlockedException("The exact stored message was not safely confirmed as sent. Automatic retry is blocked to prevent a duplicate.");
                }

                // Durability rule: checkpoint confirmed delivery before waiting on any later CDP read.
                // If Runtime.evaluate fails after this point, Stop/Start or app restart resumes at
                // the next unsent RUN ONCE message and never repeats this confirmed message.
                MessageSent?.Invoke(_currentMessage, _totalMessages, message);
                if (checkpoint is not null)
                    await checkpoint(runtimeMessage.OriginalIndex, _totalMessages, message, cancellationToken).ConfigureAwait(false);
                _sentMessages++;
                _pendingMessages = Math.Max(0, _pendingMessages - 1);
                _lastCdpEvent = "Delivery checkpoint committed";
                PublishInspector("WaitingResponse");

                SetStatus("Message accepted and checkpointed. Monitoring the same chat until the assistant response is complete...", "WaitingResponse");
                await WaitForNewResponseCompletionAsync(
                    session,
                    conversationUrl,
                    before,
                    cancellationToken).ConfigureAwait(false);
                await _safety.RecordResponseCompletedAsync(CancellationToken.None).ConfigureAwait(false);

                var stepDelaySeconds = step.EffectiveDelaySeconds(defaultDelaySeconds);
                SetStatus($"Response complete. Safety delay: {stepDelaySeconds} seconds.", "SafetyDelay");
                await Task.Delay(TimeSpan.FromSeconds(stepDelaySeconds), cancellationToken).ConfigureAwait(false);

                tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
                var recheck = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);
                ThrowIfUnsafe(recheck);
                if (recheck.IsGenerating)
                {
                    SetStatus("A new response started during the delay. Waiting without sending.", "WaitingResponse");
                    await WaitForExistingGenerationAsync(session, conversationUrl, defaultDelaySeconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (messageIndex + 1 >= messages.Count)
                {
                    if (!loop)
                    {
                        SetStatus("Plan complete. All enabled JSON messages were sent once and checkpointed; monitor stopped.", "Complete");
                        return;
                    }
                    messageIndex = 0;
                    _pendingMessages = messages.Count;
                    SetStatus("Plan cycle complete. Loop is ON; restarting from the first enabled message.", "LoopRestart");
                }
                else
                {
                    messageIndex++;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("Monitor stopped.", "Stopped");
        }
        catch (SimpleMonitorBlockedException ex)
        {
            _lastError = ex.Message;
            SetStatus($"BLOCKED — {ex.Message}", "Blocked");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            SetStatus($"BLOCKED — Chrome/session error: {ex.Message}", "Blocked");
            await ExceptionLogService.LogAsync(ex, "SimpleMonitorRunner.RunLoop").ConfigureAwait(false);
        }
    }

    private async Task WaitForExistingGenerationAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        int delaySeconds,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
            var state = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);
            ThrowIfUnsafe(state);
            if (!state.IsGenerating)
            {
                SetStatus($"Existing response finished. Waiting {delaySeconds} seconds before any send.", "SafetyDelay");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
                return;
            }
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForNewResponseCompletionAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        ChatPageState baseline,
        CancellationToken cancellationToken)
    {
        string candidateText = string.Empty;
        DateTimeOffset candidateSince = DateTimeOffset.MinValue;
        var responseObserved = false;

        while (true)
        {
            var tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
            var state = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);
            ThrowIfUnsafe(state);

            if (state.IsGenerating)
            {
                responseObserved = true;
                candidateText = string.Empty;
                candidateSince = DateTimeOffset.MinValue;
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var changed = state.AssistantCount > baseline.AssistantCount
                || (!string.IsNullOrWhiteSpace(state.LastAssistantText)
                    && !string.Equals(state.LastAssistantText, baseline.LastAssistantText, StringComparison.Ordinal));
            if (!changed && !responseObserved)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (string.IsNullOrWhiteSpace(state.LastAssistantText))
            {
                await Task.Delay(750, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!string.Equals(candidateText, state.LastAssistantText, StringComparison.Ordinal))
            {
                candidateText = state.LastAssistantText;
                candidateSince = DateTimeOffset.UtcNow;
                await Task.Delay(750, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (DateTimeOffset.UtcNow - candidateSince < TimeSpan.FromMilliseconds(1500))
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return;
        }
    }

    private async Task<ChatPageState> ReadPassiveStateResilientAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _lastCdpEvent = attempt == 1 ? "Runtime.evaluate passive read" : $"Runtime.evaluate retry {attempt - 1}/{maxAttempts - 1}";
                var state = await InvokePassiveStateReaderAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (attempt > 1) _lastCdpEvent = "Runtime.evaluate recovered";
                PublishInspector("ReadingChatState");
                return state;
            }
            catch (Exception ex) when (IsTransientRuntimeEvaluateTimeout(ex) && attempt < maxAttempts)
            {
                _passiveReadRetries++;
                _lastError = ex.Message;
                _lastCdpEvent = $"Runtime.evaluate timeout; safe passive retry {attempt}/{maxAttempts - 1}";
                StatusChanged?.Invoke($"Chrome state read timed out. Retrying safely ({attempt}/{maxAttempts - 1}) without resending any message...");
                PublishInspector("RecoveringCdpRead");
                await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientRuntimeEvaluateTimeout(ex))
            {
                _lastError = ex.Message;
                _lastCdpEvent = "Runtime.evaluate timeout exhausted";
                PublishInspector("Blocked");
                throw new SimpleMonitorBlockedException(
                    "Chrome DevTools Runtime.evaluate remained unavailable after 4 passive read attempts. Confirmed messages remain checkpointed and will not be resent; press Start after Chrome recovers to resume at the next pending message.");
            }
        }

        throw new InvalidOperationException("Passive state retry loop exited unexpectedly.");
    }

    private static Task<ChatPageState> InvokePassiveStateReaderAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
        => SimpleMonitorPassiveReadGate.RunAsync(async () =>
        {
            try
            {
                var task = (Task<ChatPageState>)(PassiveStateReader.Invoke(
                    chrome,
                    new object[] { tab, cancellationToken })
                    ?? throw new InvalidOperationException("Passive chat-state reader returned no task."));
                return await task.ConfigureAwait(false);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }, cancellationToken);

    private static bool IsTransientRuntimeEvaluateTimeout(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("Runtime.evaluate", StringComparison.OrdinalIgnoreCase)
                && message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<ChromeTab> RequireSameConversationAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        CancellationToken cancellationToken)
    {
        var tab = await session.ResolveConversationAsync(
            conversationUrl,
            openIfMissing: false,
            cancellationToken).ConfigureAwait(false);
        return tab ?? throw new SimpleMonitorBlockedException(
            "The selected chat is no longer open in the selected Chrome profile. No other chat will be used.");
    }

    private async Task<bool> HandleRateLimitIfNeededAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var active = await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
        if (!active) return false;

        SetStatus("RATE LIMITED — ChatGPT temporarily limited this profile. All physical sends are globally paused.", "RateLimited");
        await _safety.WaitForRateLimitClearAsync(
            session.Chrome,
            token => RequireSameConversationAsync(session, conversationUrl, token),
            status => SetStatus(status, "RateLimited"),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static void ThrowIfUnsafe(ChatPageState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ErrorText))
            throw new SimpleMonitorBlockedException(
                $"ChatGPT reported an error in the current chat: {state.ErrorText}. Same-chat mode will not create a replacement conversation.");
    }

    private void SetStatus(string text, string state)
    {
        StatusChanged?.Invoke(text);
        PublishInspector(state);
    }

    private void PublishInspector(string state)
        => InspectorChanged?.Invoke(new SimpleMonitorInspectorSnapshot(
            state,
            _currentMessage,
            _totalMessages,
            _sentMessages,
            _pendingMessages,
            _passiveReadRetries,
            _lastCdpEvent,
            _lastError));

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed record RuntimeMessage(int OriginalIndex, SimpleMonitorMessageStep Step);
    private sealed class SimpleMonitorBlockedException(string message) : Exception(message);
}
