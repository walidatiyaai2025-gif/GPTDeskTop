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
    private const string ConversationSetting = "SimpleMonitor.ConversationUrl";
    private const int MaxConsecutiveFreshTargetAttempts = 3;

    private static readonly MethodInfo PassiveStateReader = typeof(ChromeDevToolsService).GetMethod(
        "ReadChatStateCoreAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(ChromeDevToolsService).FullName, "ReadChatStateCoreAsync");

    private readonly object _sync = new();
    private readonly LocalDatabase? _database;
    private readonly SimpleMonitorSafetyGate _safety;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private int _passiveReadRetries;
    private int _sentMessages;
    private int _pendingMessages;
    private int _currentMessage;
    private int _totalMessages;
    private string _lastCdpEvent = "Idle";
    private string _lastError = string.Empty;

    public SimpleMonitorRunner() : this(null) { }

    public SimpleMonitorRunner(LocalDatabase? database)
    {
        _database = database;
        _safety = new SimpleMonitorSafetyGate(database);
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? ConversationChanged;
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
            throw new ArgumentException("A stable ChatGPT /c/{conversation-id} URL is required as the saved Monitor reference.", nameof(conversationUrl));
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
                // conversationUrl is deliberately only a saved/UI reference. Every explicit Start
                // establishes a fresh ChatGPT conversation before any physical send.
                _worker = Task.Run(
                    () => RunLoopAsync(session, runtimeMessages, normalizedDefaultDelay, loop, checkpoint, token),
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

            var activeTab = await CreateFreshTargetAsync(session, "Start Monitor", cancellationToken).ConfigureAwait(false);
            var messageIndex = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var runtimeMessage = messages[messageIndex];
                var step = runtimeMessage.Step;
                var message = step.Text;
                var stepDelaySeconds = step.EffectiveDelaySeconds(defaultDelaySeconds);
                var isLastStep = messageIndex + 1 >= messages.Count;
                var nextMessageIndex = isLastStep ? (loop ? 0 : -1) : messageIndex + 1;

                ChatPageState before;
                try
                {
                    activeTab = await RequireActiveTargetAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
                    before = await ReadPassiveStateResilientAsync(session.Chrome, activeTab, cancellationToken).ConfigureAwait(false);
                    await HandleRateLimitIfNeededAsync(session, activeTab, cancellationToken).ConfigureAwait(false);

                    if (HasConversationError(before))
                    {
                        activeTab = await RollOverBeforeSendAsync(
                            session,
                            activeTab,
                            $"ChatGPT reported a conversation error before message {runtimeMessage.OriginalIndex + 1}",
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (before.IsGenerating)
                    {
                        SetStatus("Fresh conversation is generating unexpectedly. Waiting before any send...", "WaitingExistingResponse");
                        var healthy = await WaitForExistingGenerationAsync(session, activeTab, defaultDelaySeconds, cancellationToken).ConfigureAwait(false);
                        if (!healthy)
                        {
                            activeTab = await RollOverBeforeSendAsync(session, activeTab, "Conversation became unavailable while waiting before send", cancellationToken).ConfigureAwait(false);
                        }
                        continue;
                    }

                    await using var sendPermit = await _safety.AcquireSendPermitAsync(
                        session.Chrome,
                        token => RequireActiveTargetAsync(session, activeTab, token),
                        (liveTab, token) => ReadPassiveStateResilientAsync(session.Chrome, liveTab, token),
                        status => SetStatus(status, "SendGate"),
                        cancellationToken).ConfigureAwait(false);

                    activeTab = sendPermit.Tab;
                    before = sendPermit.State;
                    if (HasConversationError(before))
                    {
                        activeTab = await RollOverBeforeSendAsync(session, activeTab, "Conversation failed at the final pre-send gate", cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _currentMessage = runtimeMessage.OriginalIndex + 1;
                    MessageChanged?.Invoke(_currentMessage, _totalMessages, message);
                    SetStatus($"Sending stored message {_currentMessage}/{_totalMessages} in the fresh chat...", "Sending");
                    _lastCdpEvent = "ChromeDevToolsService.SendChatMessageVerifiedAsync (stable path)";
                    PublishInspector("Sending");

                    // Conservative durable send spacing: record the attempt before entering the
                    // legacy verified sender. This preserves the global 15-second gate even when
                    // the sender later reports an uncertain result.
                    await sendPermit.RecordPhysicalAttemptAsync(CancellationToken.None).ConfigureAwait(false);

                    bool sent;
                    try
                    {
                        sent = await SimpleMonitorPassiveReadGate.RunAsync(
                            () => session.Chrome.SendChatMessageVerifiedAsync(
                                activeTab,
                                message,
                                cancellationToken,
                                requireNewTurn: true),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        var rateLimited = await TryObserveRateLimitAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
                        if (rateLimited)
                        {
                            SetStatus("RATE LIMITED — send outcome is uncertain. New Chat is forbidden until the message disposition is reconciled.", "RateLimited");
                            throw new SimpleMonitorBlockedException(
                                "ChatGPT rate limited the profile while the send outcome was uncertain. No rollover or automatic resend is allowed for this message.");
                        }

                        throw new SimpleMonitorBlockedException(
                            $"The physical send outcome is uncertain ({ex.Message}). Fresh-chat rollover is blocked for this message to prevent a duplicate.");
                    }

                    if (!sent)
                    {
                        if (await TryObserveRateLimitAsync(session, activeTab, cancellationToken).ConfigureAwait(false))
                        {
                            throw new SimpleMonitorBlockedException(
                                "ChatGPT rate limited the profile while the stable sender could not confirm delivery. New Chat and automatic resend are blocked until the message disposition is reconciled.");
                        }

                        throw new SimpleMonitorBlockedException(
                            "The stable sender did not confirm delivery. Because a physical submit may have occurred, automatic New Chat/resend is blocked for this message.");
                    }
                }
                catch (ConversationTargetException ex)
                {
                    // No sender has been entered for this iteration, so the pending message remains
                    // definitely unsent and a fresh conversation is safe.
                    activeTab = await RollOverBeforeSendAsync(session, activeTab, ex.Message, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Confirmed delivery is durable before any later read or rollover. From here onward,
                // a broken conversation may safely roll over because this message will never be sent again.
                MessageSent?.Invoke(_currentMessage, _totalMessages, message);
                if (checkpoint is not null)
                    await checkpoint(runtimeMessage.OriginalIndex, _totalMessages, message, cancellationToken).ConfigureAwait(false);
                _sentMessages++;
                _pendingMessages = Math.Max(0, _pendingMessages - 1);
                _lastCdpEvent = "Delivery checkpoint committed";
                PublishInspector("WaitingResponse");

                var stableTarget = await session.WaitForStableConversationAsync(activeTab, cancellationToken).ConfigureAwait(false);
                if (stableTarget is not null && SimpleMonitorProfileSession.TryGetConversationId(stableTarget.Url, out _))
                {
                    activeTab = stableTarget;
                    await PublishConversationAsync(activeTab.Url).ConfigureAwait(false);
                }

                var responseCompleted = false;
                try
                {
                    SetStatus("Message confirmed and checkpointed. Waiting for the response in the fresh chat...", "WaitingResponse");
                    responseCompleted = await WaitForNewResponseCompletionAsync(
                        session,
                        activeTab,
                        before,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ConversationTargetException)
                {
                    responseCompleted = false;
                }

                if (responseCompleted)
                {
                    await _safety.RecordResponseCompletedAsync(CancellationToken.None).ConfigureAwait(false);
                    SetStatus($"Response complete. Safety delay: {stepDelaySeconds} seconds.", "SafetyDelay");
                    await Task.Delay(TimeSpan.FromSeconds(stepDelaySeconds), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // The message is already confirmed/checkpointed. Preserve at least the configured
                    // delay before creating the next conversation, then advance to the next pending step.
                    SetStatus($"Conversation problem after confirmed message. Waiting {stepDelaySeconds} seconds before safe rollover...", "SafetyDelay");
                    await Task.Delay(TimeSpan.FromSeconds(stepDelaySeconds), cancellationToken).ConfigureAwait(false);
                }

                if (nextMessageIndex < 0)
                {
                    SetStatus("Plan complete. All enabled JSON messages were sent once and checkpointed; monitor stopped.", "Complete");
                    return;
                }

                if (isLastStep && loop)
                {
                    _pendingMessages = messages.Count;
                    SetStatus("Plan cycle complete. Loop is ON; continuing in the active fresh conversation.", "LoopRestart");
                }

                messageIndex = nextMessageIndex;

                if (!responseCompleted)
                {
                    activeTab = await RollOverAfterCheckpointAsync(
                        session,
                        activeTab,
                        "The previous conversation became unavailable after a confirmed checkpoint",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    activeTab = await RequireActiveTargetAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
                    var recheck = await ReadPassiveStateResilientAsync(session.Chrome, activeTab, cancellationToken).ConfigureAwait(false);
                    await HandleRateLimitIfNeededAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
                    if (HasConversationError(recheck))
                    {
                        activeTab = await RollOverAfterCheckpointAsync(
                            session,
                            activeTab,
                            "ChatGPT reported a conversation error after the confirmed checkpoint",
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (recheck.IsGenerating)
                    {
                        SetStatus("A response started during the delay. Waiting without sending.", "WaitingResponse");
                        var healthy = await WaitForExistingGenerationAsync(session, activeTab, defaultDelaySeconds, cancellationToken).ConfigureAwait(false);
                        if (!healthy)
                        {
                            activeTab = await RollOverAfterCheckpointAsync(
                                session,
                                activeTab,
                                "Conversation became unavailable while waiting after the confirmed checkpoint",
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (ConversationTargetException ex)
                {
                    activeTab = await RollOverAfterCheckpointAsync(session, activeTab, ex.Message, cancellationToken).ConfigureAwait(false);
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

    private async Task<ChromeTab> CreateFreshTargetAsync(
        SimpleMonitorProfileSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxConsecutiveFreshTargetAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SetStatus($"NEW CHAT — {reason}. Creating fresh conversation ({attempt}/{MaxConsecutiveFreshTargetAttempts})...", "CreatingFreshChat");
                _lastCdpEvent = "Target.createTarget https://chatgpt.com/";
                var tab = await session.CreateFreshConversationTabAsync(cancellationToken).ConfigureAwait(false);

                // Wait for the new root page to become readable without loading a stored message.
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var live = await session.RefreshLiveTabAsync(tab, cancellationToken).ConfigureAwait(false);
                    if (live is null)
                    {
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    try
                    {
                        _ = await InvokePassiveStateReaderAsync(session.Chrome, live, cancellationToken).ConfigureAwait(false);
                        SetStatus("Fresh ChatGPT conversation ready. Pending message remains unsent until the send gate opens.", "FreshChatReady");
                        return live;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTransientRuntimeEvaluateTimeout(ex))
                    {
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }
                }

                last = new TimeoutException("Fresh ChatGPT target did not become readable within 30 seconds.");
                await session.Chrome.CloseTabAsync(tab, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (attempt < MaxConsecutiveFreshTargetAttempts)
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }

        throw new SimpleMonitorBlockedException(
            $"A fresh ChatGPT conversation could not be established after {MaxConsecutiveFreshTargetAttempts} attempts. {last?.Message}");
    }

    private async Task<ChromeTab> RollOverBeforeSendAsync(
        SimpleMonitorProfileSession session,
        ChromeTab current,
        string reason,
        CancellationToken cancellationToken)
    {
        SetStatus($"Conversation problem before physical submit — creating a fresh chat. {reason}", "ConversationRollover");
        try { await session.Chrome.CloseTabAsync(current, CancellationToken.None).ConfigureAwait(false); } catch { }
        return await CreateFreshTargetAsync(session, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChromeTab> RollOverAfterCheckpointAsync(
        SimpleMonitorProfileSession session,
        ChromeTab current,
        string reason,
        CancellationToken cancellationToken)
    {
        SetStatus($"Confirmed message is durable — creating a fresh chat for the next pending message. {reason}", "ConversationRollover");
        try { await session.Chrome.CloseTabAsync(current, CancellationToken.None).ConfigureAwait(false); } catch { }
        return await CreateFreshTargetAsync(session, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitForExistingGenerationAsync(
        SimpleMonitorProfileSession session,
        ChromeTab activeTab,
        int delaySeconds,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ChromeTab tab;
            ChatPageState state;
            try
            {
                tab = await RequireActiveTargetAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
                state = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            }
            catch (ConversationTargetException)
            {
                return false;
            }

            await HandleRateLimitIfNeededAsync(session, tab, cancellationToken).ConfigureAwait(false);
            if (HasConversationError(state)) return false;
            if (!state.IsGenerating)
            {
                SetStatus($"Existing response finished. Waiting {delaySeconds} seconds before any send.", "SafetyDelay");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
                return true;
            }
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForNewResponseCompletionAsync(
        SimpleMonitorProfileSession session,
        ChromeTab activeTab,
        ChatPageState baseline,
        CancellationToken cancellationToken)
    {
        string candidateText = string.Empty;
        DateTimeOffset candidateSince = DateTimeOffset.MinValue;
        var responseObserved = false;

        while (true)
        {
            var tab = await RequireActiveTargetAsync(session, activeTab, cancellationToken).ConfigureAwait(false);
            var state = await ReadPassiveStateResilientAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            await HandleRateLimitIfNeededAsync(session, tab, cancellationToken).ConfigureAwait(false);
            if (HasConversationError(state)) return false;

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

            return true;
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
                StatusChanged?.Invoke($"Chrome state read timed out. Retrying safely ({attempt}/{maxAttempts - 1}) before any message mutation...");
                PublishInspector("RecoveringCdpRead");
                await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientRuntimeEvaluateTimeout(ex))
            {
                _lastError = ex.Message;
                _lastCdpEvent = "Runtime.evaluate timeout exhausted";
                PublishInspector("ConversationRollover");
                throw new ConversationTargetException(
                    "Chrome DevTools passive state remained unavailable before the next send.", ex);
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

    private static async Task<ChromeTab> RequireActiveTargetAsync(
        SimpleMonitorProfileSession session,
        ChromeTab activeTab,
        CancellationToken cancellationToken)
    {
        try
        {
            var live = await session.RefreshLiveTabAsync(activeTab, cancellationToken).ConfigureAwait(false);
            return live ?? throw new ConversationTargetException("The active fresh ChatGPT tab is no longer available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConversationTargetException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversationTargetException("The active ChatGPT conversation could not be resolved.", ex);
        }
    }

    private async Task<bool> HandleRateLimitIfNeededAsync(
        SimpleMonitorProfileSession session,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var active = await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
        if (!active) return false;

        SetStatus("RATE LIMITED — profile/account protection is active. New Chat will NOT be used as a bypass.", "RateLimited");
        await _safety.WaitForRateLimitClearAsync(
            session.Chrome,
            token => RequireActiveTargetAsync(session, tab, token),
            status => SetStatus(status, "RateLimited"),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryObserveRateLimitAsync(
        SimpleMonitorProfileSession session,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return _safety.IsRateLimitActive;
        }
    }

    private async Task PublishConversationAsync(string conversationUrl)
    {
        if (!SimpleMonitorProfileSession.TryGetConversationId(conversationUrl, out _)) return;
        if (_database is not null)
            await _database.SetSettingAsync(ConversationSetting, conversationUrl, CancellationToken.None).ConfigureAwait(false);
        ConversationChanged?.Invoke(conversationUrl);
        SetStatus($"Fresh conversation locked: {conversationUrl}", "ConversationLocked");
    }

    private static bool HasConversationError(ChatPageState state)
        => !string.IsNullOrWhiteSpace(state.ErrorText);

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
    private sealed class ConversationTargetException(string message, Exception? inner = null) : Exception(message, inner);
}
