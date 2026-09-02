using System.Reflection;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class SimpleMonitorRunner : IAsyncDisposable
{
    private static readonly MethodInfo PassiveStateReader = typeof(ChromeDevToolsService).GetMethod(
        "ReadChatStateCoreAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(ChromeDevToolsService).FullName, "ReadChatStateCoreAsync");

    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public event Action<string>? StatusChanged;
    public event Action<int, int, string>? MessageChanged;
    public bool IsRunning { get { lock (_sync) return _worker is { IsCompleted: false }; } }

    public Task StartAsync(
        SimpleMonitorProfileSession session,
        string conversationUrl,
        IReadOnlyList<string> messages,
        int delaySeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!SimpleMonitorProfileSession.TryGetConversationId(conversationUrl, out _))
            throw new ArgumentException("A stable ChatGPT /c/{conversation-id} URL is required.", nameof(conversationUrl));
        if (messages is null || messages.Count == 0 || messages.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty stored message is required.", nameof(messages));

        var normalizedDelay = Math.Clamp(delaySeconds, 15, 3600);
        var exactMessages = messages.ToArray();
        lock (_sync)
        {
            if (_worker is { IsCompleted: false })
                throw new InvalidOperationException("The monitor-only runtime is already running.");

            _cancellation?.Dispose();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellation.Token;
            _worker = Task.Run(() => RunLoopAsync(session, conversationUrl, exactMessages, normalizedDelay, token), token);
        }
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
        IReadOnlyList<string> messages,
        int delaySeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            StatusChanged?.Invoke("Connecting to the selected Chrome profile...");
            await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var tab = await session.ResolveConversationAsync(conversationUrl, openIfMissing: true, cancellationToken).ConfigureAwait(false)
                ?? throw new SimpleMonitorBlockedException("The selected ChatGPT conversation could not be opened in this profile.");

            var initial = await ReadPassiveStateAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            ThrowIfUnsafe(initial);
            if (initial.IsGenerating)
            {
                StatusChanged?.Invoke("The selected chat is already generating. Waiting for it to finish...");
                await WaitForExistingGenerationAsync(session, conversationUrl, delaySeconds, cancellationToken).ConfigureAwait(false);
            }

            var messageIndex = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
                var before = await ReadPassiveStateAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                ThrowIfUnsafe(before);
                if (before.IsGenerating)
                {
                    StatusChanged?.Invoke("ChatGPT is generating. Send is blocked until the response finishes.");
                    await WaitForExistingGenerationAsync(session, conversationUrl, delaySeconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var message = messages[messageIndex];
                MessageChanged?.Invoke(messageIndex + 1, messages.Count, message);
                StatusChanged?.Invoke($"Sending stored message {messageIndex + 1}/{messages.Count} to the same chat...");

                var sent = await session.Chrome.SendChatMessageVerifiedAsync(
                    tab,
                    message,
                    cancellationToken,
                    requireNewTurn: true).ConfigureAwait(false);
                if (!sent)
                    throw new SimpleMonitorBlockedException("The exact stored message was not safely confirmed as sent. Automatic retry is blocked to prevent a duplicate.");

                StatusChanged?.Invoke("Message accepted. Monitoring the same chat until the assistant response is complete...");
                await WaitForNewResponseCompletionAsync(
                    session,
                    conversationUrl,
                    before,
                    cancellationToken).ConfigureAwait(false);

                StatusChanged?.Invoke($"Response complete. Safety delay: {delaySeconds} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);

                tab = await RequireSameConversationAsync(session, conversationUrl, cancellationToken).ConfigureAwait(false);
                var recheck = await ReadPassiveStateAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                ThrowIfUnsafe(recheck);
                if (recheck.IsGenerating)
                {
                    StatusChanged?.Invoke("A new response started during the delay. Waiting without sending.");
                    await WaitForExistingGenerationAsync(session, conversationUrl, delaySeconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                messageIndex = (messageIndex + 1) % messages.Count;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke("Monitor stopped.");
        }
        catch (SimpleMonitorBlockedException ex)
        {
            StatusChanged?.Invoke($"BLOCKED — {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"BLOCKED — Chrome/session error: {ex.Message}");
            await ExceptionLogService.LogAsync(ex, "SimpleMonitorRunner.RunLoop").ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (_worker is { IsCompleted: false })
                {
                    // The task completes immediately after this finally block. Keep the cancellation
                    // source intact so StopAsync can still join/dispose it deterministically.
                }
            }
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
            var state = await ReadPassiveStateAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
            ThrowIfUnsafe(state);
            if (!state.IsGenerating)
            {
                StatusChanged?.Invoke($"Existing response finished. Waiting {delaySeconds} seconds before any send.");
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
            var state = await ReadPassiveStateAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
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

    private static Task<ChatPageState> ReadPassiveStateAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        try
        {
            return (Task<ChatPageState>)(PassiveStateReader.Invoke(
                chrome,
                new object[] { tab, cancellationToken })
                ?? throw new InvalidOperationException("Passive chat-state reader returned no task."));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void ThrowIfUnsafe(ChatPageState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ErrorText))
            throw new SimpleMonitorBlockedException(
                $"ChatGPT reported an error in the current chat: {state.ErrorText}. Same-chat mode will not create a replacement conversation.");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed class SimpleMonitorBlockedException(string message) : Exception(message);
}
