using System.Net.WebSockets;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChatGptMonitorService
{
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly MonitoringConfig _config;
    private readonly ModelRoutingService _modelRouting = new();
    private readonly object _sync = new();
    private readonly Dictionary<long, MonitorRuntime> _running = new();

    public event Action<long, string>? Activity;
    public event Action? HistoryChanged;
    public event Action? RunningStateChanged;
    public event Action<long, string, string, bool>? ResponseReceived;

    public bool IsRunning { get { lock (_sync) return _running.Count > 0; } }

    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;
    }

    public bool IsMonitorRunning(long monitorId)
    {
        lock (_sync) return _running.ContainsKey(monitorId);
    }

    public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
    {
        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before starting it.");
        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");

        lock (_sync)
        {
            if (_running.ContainsKey(monitor.Id)) return Task.CompletedTask;
            var cts = new CancellationTokenSource();
            var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token));
            _running.Add(monitor.Id, new MonitorRuntime(cts, worker));
        }

        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}");
        RunningStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task StopMonitorAsync(long monitorId)
    {
        MonitorRuntime? runtime;
        lock (_sync)
        {
            if (!_running.Remove(monitorId, out runtime)) return;
        }

        runtime.Cancellation.Cancel();
        try { await runtime.Worker; }
        catch (OperationCanceledException) { }
        finally
        {
            runtime.Cancellation.Dispose();
            Activity?.Invoke(monitorId, "Stopped.");
            RunningStateChanged?.Invoke();
        }
    }

    public async Task StopAllAsync()
    {
        long[] ids;
        lock (_sync) ids = _running.Keys.ToArray();
        await Task.WhenAll(ids.Select(StopMonitorAsync));
    }

    private async Task MonitorLoopAsync(SavedMonitor monitor, ChromeTab tab, CancellationToken cancellationToken)
    {
        var timerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        var replyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        var noResponseSeconds = await _database.GetIntSettingAsync("NoResponseRefreshSeconds", 180, 30, 3600, cancellationToken);
        var transientFailures = 0;

        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Timer {timerSeconds}s | Delay {replyDelaySeconds}s | No-response refresh {noResponseSeconds}s | Rotation {(monitor.ConversationRotationEnabled ? "ON" : "OFF")} | Model routing {(monitor.ModelRoutingEnabled ? "ON" : "OFF")} | Reply: {monitor.AutoReply}");

        try
        {
            var initial = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);
            var lastHandledText = GetEffectiveResponse(initial);
            var candidateText = string.Empty;
            var candidateSince = DateTimeOffset.MinValue;
            var lastResponseActivity = DateTimeOffset.UtcNow;

            // Apply the preferred model once at monitor startup. Auto means no UI interaction.
            await ApplyModelRouteAsync(monitor, tab, recovery: false, contextRotation: false, cancellationToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(timerSeconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var prefix = $"[{monitor.Title}]";
                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                    var text = GetEffectiveResponse(state);
                    transientFailures = 0;

                    noResponseSeconds = await _database.GetIntSettingAsync("NoResponseRefreshSeconds", 180, 30, 3600, cancellationToken);
                    if ((DateTimeOffset.UtcNow - lastResponseActivity).TotalSeconds >= noResponseSeconds)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} No new response for {noResponseSeconds}s. Refreshing this tab...");
                        await _chrome.ReloadTabAsync(tab, cancellationToken);
                        await _database.AddLogAsync("System", "Page.reload", $"No assistant response for {noResponseSeconds} seconds.", "NoResponseRefresh", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                        HistoryChanged?.Invoke();
                        lastResponseActivity = DateTimeOffset.UtcNow;
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        await Task.Delay(Math.Max(1000, _config.DelayAfterSendMilliseconds), cancellationToken);
                        continue;
                    }

                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || string.Equals(text, lastHandledText, StringComparison.Ordinal))
                    {
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        continue;
                    }

                    if (!string.Equals(candidateText, text, StringComparison.Ordinal))
                    {
                        candidateText = text;
                        candidateSince = DateTimeOffset.UtcNow;
                        Activity?.Invoke(monitor.Id, $"{prefix} New response detected...");
                        continue;
                    }

                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds)
                        continue;

                    lastHandledText = text;
                    lastResponseActivity = DateTimeOffset.UtcNow;
                    candidateText = string.Empty;
                    candidateSince = DateTimeOffset.MinValue;

                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText) || IsErrorResponse(text);
                    await _database.AddLogAsync("Inbound", string.Empty, text, IsConversationContextLimit(text) ? "ConversationLimit" : isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke();
                    ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);

                    if (monitor.ConversationRotationEnabled && IsConversationContextLimit(text))
                    {
                        if (monitor.MaxConversationRotations > 0 && monitor.RotationCount >= monitor.MaxConversationRotations)
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} Conversation limit detected, but maximum rotations ({monitor.MaxConversationRotations}) has been reached. Monitor remains stopped on the current chat.");
                            await _database.AddLogAsync("System", monitor.NewChatStartMessage, text, "RotationLimitReached", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            continue;
                        }

                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT reported a conversation/context limit. Rotating to a new chat...");
                        if (monitor.NewChatDelaySeconds > 0)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.NewChatDelaySeconds, 0, 600)), cancellationToken);

                        var oldTab = tab;
                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                        await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken);

                        var handoffService = new ConversationHandoffService(_database);
                        var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken);
                        var startMessage = string.IsNullOrWhiteSpace(handoffMessage)
                            ? (string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage)
                            : handoffMessage;
                        var sent = await _chrome.SendChatMessageAsync(newTab, startMessage, cancellationToken);
                        if (!sent)
                            throw new InvalidOperationException("New ChatGPT chat opened but the rotation start message could not be sent.");

                        monitor.RotationCount++;
                        monitor.TabId = newTab.Id;
                        monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? $"ChatGPT Chat #{monitor.RotationCount + 1}" : newTab.Title;
                        monitor.Url = newTab.Url;
                        await _database.SaveMonitorAsync(monitor, cancellationToken);
                        await _database.AddConversationRotationAsync(monitor.Id, oldTab.Id, newTab.Id, "ConversationContextLimit", startMessage, cancellationToken);
                        await _database.AddLogAsync("System", startMessage, text, "RotatedToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        await _database.AddLogAsync("Outbound", startMessage, string.Empty, "RotationStartSent", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        HistoryChanged?.Invoke();

                        tab = newTab;
                        lastHandledText = string.Empty;
                        lastResponseActivity = DateTimeOffset.UtcNow;
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        Activity?.Invoke(monitor.Id, $"{prefix} Rotation #{monitor.RotationCount} complete. Monitoring the new ChatGPT conversation under the same Monitor ID.");

                        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); }
                        catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after rotation: {closeEx.Message}"); }

                        if (monitor.RotationCooldownSeconds > 0)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken);
                        continue;
                    }

                    if (isError && IsDeliveryTimeout(text))
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat...");
                        var recoveryMessage = await _database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل";
                        var oldTab = tab;
                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                        await ApplyModelRouteAsync(monitor, newTab, recovery: true, contextRotation: false, cancellationToken);

                        var sent = await _chrome.SendChatMessageAsync(newTab, recoveryMessage, cancellationToken);
                        if (!sent) throw new InvalidOperationException("New chat opened but the recovery message could not be sent.");

                        monitor.TabId = newTab.Id;
                        monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? "Recovered ChatGPT Chat" : newTab.Title;
                        monitor.Url = newTab.Url;
                        await _database.SaveMonitorAsync(monitor, cancellationToken);
                        await _database.AddLogAsync("System", recoveryMessage, text, "RecoveredToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        await _database.AddLogAsync("Outbound", recoveryMessage, string.Empty, "RecoverySent", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        HistoryChanged?.Invoke();

                        tab = newTab;
                        lastHandledText = string.Empty;
                        lastResponseActivity = DateTimeOffset.UtcNow;
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}.");
                        await _chrome.CloseTabAsync(oldTab, cancellationToken);
                        continue;
                    }

                    if (isError)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Error saved. Refreshing only this tab...");
                        try
                        {
                            await _chrome.ReloadTabAsync(tab, cancellationToken);
                            await _database.AddLogAsync("System", "Page.reload", text, "RefreshedAfterError", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            lastResponseActivity = DateTimeOffset.UtcNow;
                            await Task.Delay(Math.Max(1500, _config.DelayAfterSendMilliseconds), cancellationToken);
                        }
                        catch (Exception refreshEx) when (refreshEx is not OperationCanceledException && !IsTransientChromeException(refreshEx))
                        {
                            ExceptionLogService.Log(refreshEx, "Monitor.RefreshAfterError", monitor.Id, tab.Id, monitor.Title);
                            await _database.AddLogAsync("System", "Page.reload", refreshEx.ToString(), "RefreshFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                        }
                        continue;
                    }

                    if (replyDelaySeconds > 0)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before auto reply...");
                        await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken);
                        var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken);
                        var latestText = GetEffectiveResponse(recheck);
                        if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal))
                        {
                            await _database.AddLogAsync("System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            continue;
                        }
                    }

                    var autoSent = await _chrome.SendChatMessageAsync(tab, monitor.AutoReply, cancellationToken);
                    await _database.AddLogAsync("Outbound", monitor.AutoReply, string.Empty, autoSent ? "Sent" : "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke();
                    lastResponseActivity = DateTimeOffset.UtcNow;
                    await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransientChromeException(ex))
                {
                    transientFailures++;
                    if (transientFailures <= 3)
                        Activity?.Invoke(monitor.Id, $"CDP transient disconnect ({transientFailures}/3): {ex.GetType().Name}. Retrying...");
                    else if (transientFailures == 4)
                        Activity?.Invoke(monitor.Id, "CDP is temporarily unavailable. Background retry continues; this is not counted as an application crash.");

                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 750 * transientFailures)), cancellationToken);
                }
                catch (Exception ex)
                {
                    Activity?.Invoke(monitor.Id, $"Monitor exception logged: {ex.GetType().Name}: {ex.Message}");
                    await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.MonitorLoop", monitor.Id, tab.Id, monitor.Title);
                    HistoryChanged?.Invoke();
                    await Task.Delay(1500, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Activity?.Invoke(monitor.Id, "Monitor cancellation requested; stopping cleanly.");
        }
        catch (Exception ex) when (IsTransientChromeException(ex))
        {
            Activity?.Invoke(monitor.Id, $"Chrome connection unavailable during startup. Monitor remains stopped and can be started again: {ex.Message}");
        }
        catch (Exception ex)
        {
            await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.WorkerFatal", monitor.Id, tab.Id, monitor.Title);
            Activity?.Invoke(monitor.Id, $"Monitor worker stopped by exception: {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                if (_running.TryGetValue(monitor.Id, out var current) && current.Cancellation.Token == cancellationToken)
                    _running.Remove(monitor.Id);
            }
            RunningStateChanged?.Invoke();
        }
    }

    private async Task ApplyModelRouteAsync(SavedMonitor monitor, ChromeTab tab, bool recovery, bool contextRotation, CancellationToken cancellationToken)
    {
        if (!monitor.ModelRoutingEnabled) return;
        var decision = _modelRouting.Choose(monitor, recovery, contextRotation);
        if (string.Equals(decision.PreferredModel, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Model routing: Auto; keeping ChatGPT's current model.");
            return;
        }

        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Selecting model '{decision.PreferredModel}' ({decision.Reason})...");
        var selected = await _chrome.TrySelectModelAsync(tab, decision.PreferredModel, cancellationToken);
        if (selected)
        {
            await _database.AddLogAsync("System", decision.PreferredModel, string.Empty, "ModelSelected", monitor.Id, tab.Id, monitor.Title, cancellationToken);
            HistoryChanged?.Invoke();
            return;
        }

        if (!string.Equals(decision.FallbackModel, decision.PreferredModel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decision.FallbackModel, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Preferred model '{decision.PreferredModel}' was not selectable. Trying configured fallback '{decision.FallbackModel}' once.");
            var fallbackSelected = await _chrome.TrySelectModelAsync(tab, decision.FallbackModel, cancellationToken);
            await _database.AddLogAsync("System", decision.FallbackModel, string.Empty, fallbackSelected ? "FallbackModelSelected" : "ModelSelectionSkipped", monitor.Id, tab.Id, monitor.Title, cancellationToken);
            HistoryChanged?.Invoke();
        }
        else
        {
            await _database.AddLogAsync("System", decision.PreferredModel, string.Empty, "ModelSelectionSkipped", monitor.Id, tab.Id, monitor.Title, cancellationToken);
            HistoryChanged?.Invoke();
        }
    }

    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { return await _chrome.GetChatStateAsync(tab, cancellationToken); }
            catch (Exception ex) when (IsTransientChromeException(ex))
            {
                last = ex;
                Activity?.Invoke(monitorId, $"Initial Chrome/CDP connection retry {attempt}/3: {ex.GetType().Name}");
                await Task.Delay(500 * attempt, cancellationToken);
            }
        }
        throw last ?? new InvalidOperationException("Unable to read the ChatGPT tab state.");
    }

    private static bool IsTransientChromeException(Exception ex)
        => ex is WebSocketException
           || ex is TimeoutException
           || ex is TaskCanceledException
           || ex is IOException
           || ex.Message.Contains("Chrome closed the DevTools connection", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("connection was forcibly closed", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("unable to connect", StringComparison.OrdinalIgnoreCase);

    private static string GetEffectiveResponse(ChatPageState state)
        => !string.IsNullOrWhiteSpace(state.ErrorText) ? state.ErrorText.Trim() : state.LastAssistantText.Trim();

    private static bool IsDeliveryTimeout(string text)
        => text.Contains("message delivery timed out", StringComparison.OrdinalIgnoreCase);

    private static bool IsConversationContextLimit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] markers =
        {
            "conversation is too long", "conversation is too large", "context length",
            "context window", "maximum context", "conversation limit", "start a new chat",
            "this conversation has reached", "reached the maximum length",
            "المحادثة طويلة جدًا", "طول المحادثة", "حد المحادثة", "ابدأ محادثة جديدة"
        };
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsErrorResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] markers =
        {
            "message delivery timed out", "something went wrong", "there was an error", "network error",
            "failed to generate", "error generating", "unable to generate", "unable to load",
            "حدث خطأ", "خطأ في الشبكة", "تعذر إنشاء", "تعذر تحميل"
        };
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record MonitorRuntime(CancellationTokenSource Cancellation, Task Worker);
}
