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
    { _chrome = chrome; _database = database; _config = config; }

    public bool IsMonitorRunning(long monitorId) { lock (_sync) return _running.ContainsKey(monitorId); }

    public async Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(tab);
        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before starting it.");
        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            throw new InvalidOperationException("The saved monitor URL is not a stable ChatGPT conversation identity.");
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");

        var savedMonitors = await _database.GetSavedMonitorsAsync();
        if (MonitorConversationOwnership.IsDuplicateOwner(monitor.Id, savedMonitors))
        {
            const string message = "Saved monitor conversation ownership is ambiguous. Resolve duplicate monitor rows before starting this monitor.";
            await _database.AddLogAsync(
                "System",
                string.Empty,
                message,
                "MonitorStartDuplicateConversationOwnership",
                monitor.Id,
                monitor.TabId,
                monitor.Title);
            HistoryChanged?.Invoke();
            Activity?.Invoke(monitor.Id, message);
            return;
        }

        lock (_sync)
        {
            if (_running.ContainsKey(monitor.Id)) return;
            var cts = new CancellationTokenSource();
            var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token));
            _running.Add(monitor.Id, new MonitorRuntime(cts, worker));
        }
        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}");
        RunningStateChanged?.Invoke();
    }

    public async Task StopMonitorAsync(long monitorId)
    {
        MonitorRuntime? runtime; lock (_sync) { if (!_running.Remove(monitorId, out runtime)) return; }
        runtime.Cancellation.Cancel(); try { await runtime.Worker; } catch (OperationCanceledException) { } finally { runtime.Cancellation.Dispose(); Activity?.Invoke(monitorId, "Stopped."); RunningStateChanged?.Invoke(); }
    }

    public async Task StopAllAsync() { long[] ids; lock (_sync) ids = _running.Keys.ToArray(); await Task.WhenAll(ids.Select(StopMonitorAsync)); }

    private async Task MonitorLoopAsync(SavedMonitor monitor, ChromeTab tab, CancellationToken cancellationToken)
    {
        var timerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        var replyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        var noResponseSeconds = await _database.GetIntSettingAsync("NoResponseRefreshSeconds", 180, 30, 3600, cancellationToken);
        var rotateAfterMessages = await _database.GetIntSettingAsync("RotateAfterAssistantMessages", 0, 0, 10000, cancellationToken);
        var messageCountRotationStartMessage = await _database.GetSettingAsync("MessageCountRotationStartMessage", cancellationToken) ?? "كمل";
        var transientFailures = 0;
        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Timer {timerSeconds}s | Delay {replyDelaySeconds}s | No-response refresh {noResponseSeconds}s | Rotation {(monitor.ConversationRotationEnabled ? "ON" : "OFF")} | Count rotation {(rotateAfterMessages > 0 ? $"{rotateAfterMessages} assistant messages" : "OFF")} | Model routing {(monitor.ModelRoutingEnabled ? "ON" : "OFF")} | Reply: {monitor.AutoReply}");
        try
        {
            var initial = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);
            var initialText = GetEffectiveResponse(initial);
            var initialCountRotationDue = monitor.ConversationRotationEnabled
                && rotateAfterMessages > 0
                && !initial.IsGenerating
                && !string.IsNullOrWhiteSpace(initialText)
                && string.IsNullOrWhiteSpace(initial.ErrorText)
                && !IsErrorResponse(initialText)
                && !IsConversationContextLimit(initialText)
                && initial.AssistantCount >= rotateAfterMessages;
            var lastHandledText = initialCountRotationDue ? string.Empty : initialText;
            var candidateText = string.Empty;
            var candidateSince = DateTimeOffset.MinValue;
            var lastResponseActivity = DateTimeOffset.UtcNow;
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
                    rotateAfterMessages = await _database.GetIntSettingAsync("RotateAfterAssistantMessages", 0, 0, 10000, cancellationToken);
                    messageCountRotationStartMessage = await _database.GetSettingAsync("MessageCountRotationStartMessage", cancellationToken) ?? "كمل";
                    var messageCountThresholdReached = monitor.ConversationRotationEnabled
                        && rotateAfterMessages > 0
                        && !state.IsGenerating
                        && !string.IsNullOrWhiteSpace(text)
                        && string.IsNullOrWhiteSpace(state.ErrorText)
                        && !IsErrorResponse(text)
                        && !IsConversationContextLimit(text)
                        && state.AssistantCount >= rotateAfterMessages;
                    var rotationSlotAvailable = monitor.MaxConversationRotations <= 0 || monitor.RotationCount < monitor.MaxConversationRotations;
                    var messageCountRotationDue = messageCountThresholdReached && rotationSlotAvailable;

                    if ((DateTimeOffset.UtcNow - lastResponseActivity).TotalSeconds >= noResponseSeconds)
                    { Activity?.Invoke(monitor.Id, $"{prefix} No new response for {noResponseSeconds}s. Refreshing this tab..."); await _chrome.ReloadTabAsync(tab, cancellationToken); await _database.AddLogAsync("System", "Page.reload", $"No assistant response for {noResponseSeconds} seconds.", "NoResponseRefresh", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; await Task.Delay(Math.Max(1000, _config.DelayAfterSendMilliseconds), cancellationToken); continue; }
                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || (string.Equals(text, lastHandledText, StringComparison.Ordinal) && !messageCountRotationDue)) { candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; continue; }
                    if (!string.Equals(candidateText, text, StringComparison.Ordinal)) { candidateText = text; candidateSince = DateTimeOffset.UtcNow; Activity?.Invoke(monitor.Id, $"{prefix} New response detected..."); continue; }
                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;
                    lastHandledText = text; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText) || IsErrorResponse(text); await _database.AddLogAsync("Inbound", string.Empty, text, IsConversationContextLimit(text) ? "ConversationLimit" : isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);

                    if (messageCountThresholdReached && !rotationSlotAvailable)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Assistant count {state.AssistantCount} reached the configured rotation threshold {rotateAfterMessages}, but maximum rotations ({monitor.MaxConversationRotations}) has been reached. Continuing on the current chat.");
                        await _database.AddLogAsync("System", messageCountRotationStartMessage, text, "MessageCountRotationLimitReached", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();
                    }

                    if (messageCountRotationDue)
                    {
                        var oldTab = tab;
                        var newTab = await RotateByMessageCountAsync(monitor, oldTab, state.AssistantCount, rotateAfterMessages, text, messageCountRotationStartMessage, cancellationToken);
                        if (newTab is null)
                        {
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; lastResponseActivity = DateTimeOffset.UtcNow;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }

                        tab = newTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                        continue;
                    }

                    if (monitor.ConversationRotationEnabled && IsConversationContextLimit(text))
                    {
                        if (monitor.MaxConversationRotations > 0 && monitor.RotationCount >= monitor.MaxConversationRotations) { Activity?.Invoke(monitor.Id, $"{prefix} Conversation limit detected, but maximum rotations ({monitor.MaxConversationRotations}) has been reached. Monitor remains stopped on the current chat."); await _database.AddLogAsync("System", monitor.NewChatStartMessage, text, "RotationLimitReached", monitor.Id, tab.Id, monitor.Title, cancellationToken); continue; }
                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT reported a conversation/context limit. Rotating to a new chat..."); if (monitor.NewChatDelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.NewChatDelaySeconds, 0, 600)), cancellationToken);
                        var oldTab = tab; var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken); await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken); await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken); await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);
                        var handoffService = new ConversationHandoffService(_database); var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken); var startMessage = string.IsNullOrWhiteSpace(handoffMessage) ? (string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage) : handoffMessage; var sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);
                        if (!sent)
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} Rotation handoff is still not accepted. Closing the unused new tab and retrying the same rotation later.");
                            await _database.AddLogAsync("System", startMessage, text, "RotationHandoffDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();
                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred rotation tab close failed transiently: {closeEx.Message}"); }
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; lastResponseActivity = DateTimeOffset.UtcNow;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        monitor.RotationCount++; monitor.TabId = newTab.Id; monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? $"ChatGPT Chat #{monitor.RotationCount + 1}" : newTab.Title; monitor.Url = newTab.Url; await _database.SaveMonitorAsync(monitor, cancellationToken); await _database.AddConversationRotationAsync(monitor.Id, oldTab.Id, newTab.Id, "ConversationContextLimit", startMessage, cancellationToken); await _database.AddLogAsync("System", startMessage, text, "RotatedToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken); await _database.AddLogAsync("Outbound", startMessage, string.Empty, "RotationStartSent", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); tab = newTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"{prefix} Rotation #{monitor.RotationCount} complete. Monitoring the new ChatGPT conversation under the same Monitor ID.");
                        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after rotation: {closeEx.Message}"); }
                        if (monitor.RotationCooldownSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken); continue;
                    }
                    if (isError && IsDeliveryTimeout(text))
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat..."); var recoveryMessage = await _database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل"; var oldTab = tab; var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken); await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken); await ApplyModelRouteAsync(monitor, newTab, recovery: true, contextRotation: false, cancellationToken); await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken); var sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);
                        if (!sent)
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} Recovery message is still not accepted. Closing the unused recovery tab and retrying later.");
                            await _database.AddLogAsync("System", recoveryMessage, text, "RecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();
                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred recovery tab close failed transiently: {closeEx.Message}"); }
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; lastResponseActivity = DateTimeOffset.UtcNow;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        monitor.TabId = newTab.Id; monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? "Recovered ChatGPT Chat" : newTab.Title; monitor.Url = newTab.Url; await _database.SaveMonitorAsync(monitor, cancellationToken); await _database.AddLogAsync("System", recoveryMessage, text, "RecoveredToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken); await _database.AddLogAsync("Outbound", recoveryMessage, string.Empty, "RecoverySent", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); tab = newTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;
                    }
                    if (isError)
                    { Activity?.Invoke(monitor.Id, $"{prefix} Error saved. Refreshing only this tab..."); try { await _chrome.ReloadTabAsync(tab, cancellationToken); await _database.AddLogAsync("System", "Page.reload", text, "RefreshedAfterError", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); lastResponseActivity = DateTimeOffset.UtcNow; await Task.Delay(Math.Max(1500, _config.DelayAfterSendMilliseconds), cancellationToken); } catch (Exception refreshEx) when (refreshEx is not OperationCanceledException && !IsTransientChromeException(refreshEx)) { ExceptionLogService.Log(refreshEx, "Monitor.RefreshAfterError", monitor.Id, tab.Id, monitor.Title); await _database.AddLogAsync("System", "Page.reload", refreshEx.ToString(), "RefreshFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); } continue; }
                    if (replyDelaySeconds > 0)
                    { Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before auto reply..."); await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken); var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken); var latestText = GetEffectiveResponse(recheck); if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal)) { await _database.AddLogAsync("System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); continue; } }
                    var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply, allowRecoveryReload: false, cancellationToken); await _database.AddLogAsync("Outbound", monitor.AutoReply, string.Empty, autoSent ? "Sent" : "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); lastResponseActivity = DateTimeOffset.UtcNow; await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (IsTransientChromeException(ex)) { transientFailures++; if (transientFailures <= 3) Activity?.Invoke(monitor.Id, $"CDP transient disconnect ({transientFailures}/3): {ex.GetType().Name}. Retrying..."); else if (transientFailures == 4) Activity?.Invoke(monitor.Id, "CDP is temporarily unavailable. Background retry continues; this is not counted as an application crash."); await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 750 * transientFailures)), cancellationToken); }
                catch (Exception ex) { Activity?.Invoke(monitor.Id, $"Monitor exception logged: {ex.GetType().Name}: {ex.Message}"); await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.MonitorLoop", monitor.Id, tab.Id, monitor.Title); HistoryChanged?.Invoke(); await Task.Delay(1500, cancellationToken); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Activity?.Invoke(monitor.Id, "Monitor cancellation requested; stopping cleanly."); }
        catch (Exception ex) when (IsTransientChromeException(ex)) { Activity?.Invoke(monitor.Id, $"Chrome connection unavailable during startup. Monitor remains stopped and can be started again: {ex.Message}"); }
        catch (Exception ex) { await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.WorkerFatal", monitor.Id, tab.Id, monitor.Title); Activity?.Invoke(monitor.Id, $"Monitor worker stopped by exception: {ex.Message}"); }
        finally { lock (_sync) { if (_running.TryGetValue(monitor.Id, out var current) && current.Cancellation.Token == cancellationToken) _running.Remove(monitor.Id); } RunningStateChanged?.Invoke(); }
    }

    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)
    {
        var prefix = $"[{monitor.Title}]";
        var startMessage = string.IsNullOrWhiteSpace(configuredStartMessage) ? "كمل" : configuredStartMessage.Trim();
        Activity?.Invoke(monitor.Id, $"{prefix} Assistant count {assistantCount} reached threshold {threshold}. Opening a new ChatGPT conversation...");

        if (monitor.NewChatDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.NewChatDelaySeconds, 0, 600)), cancellationToken);

        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);
        await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken);
        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);

        var sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);
        if (!sent)
        {
            Activity?.Invoke(monitor.Id, $"{prefix} Message-count rotation start message was not verified. Closing the unused new tab and retrying later.");
            await _database.AddLogAsync("System", startMessage, triggerText, "MessageCountRotationDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred message-count rotation tab close failed transiently: {closeEx.Message}"); }
            return null;
        }

        monitor.RotationCount++;
        monitor.TabId = newTab.Id;
        monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? $"ChatGPT Chat #{monitor.RotationCount + 1}" : newTab.Title;
        monitor.Url = newTab.Url;
        await _database.SaveMonitorAsync(monitor, cancellationToken);
        await _database.AddConversationRotationAsync(monitor.Id, oldTab.Id, newTab.Id, "AssistantMessageCount", startMessage, cancellationToken);
        await _database.AddLogAsync("System", startMessage, triggerText, "RotatedByMessageCount", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
        await _database.AddLogAsync("Outbound", startMessage, string.Empty, "MessageCountRotationStartSent", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
        HistoryChanged?.Invoke();
        Activity?.Invoke(monitor.Id, $"{prefix} Message-count rotation #{monitor.RotationCount} complete. Same Monitor ID is now bound to the new conversation.");

        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after message-count rotation: {closeEx.Message}"); }
        if (monitor.RotationCooldownSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken);

        return newTab;
    }

    private async Task WaitForChatReadyAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)
    { var deadline = DateTimeOffset.UtcNow.AddSeconds(60); Exception? last = null; while (DateTimeOffset.UtcNow < deadline) { cancellationToken.ThrowIfCancellationRequested(); try { var state = await _chrome.GetChatStateAsync(tab, cancellationToken); if (!state.IsGenerating) { Activity?.Invoke(monitorId, $"[{tab.Title}] New Chat page is available; waiting for verified composer delivery."); return; } } catch (Exception ex) when (IsTransientChromeException(ex)) { last = ex; } await Task.Delay(500, cancellationToken); } throw new TimeoutException($"New Chat was not available within 60 seconds.{(last is null ? string.Empty : $" Last CDP error: {last.Message}")}"); }

    private async Task<bool> SendWhenReadyAsync(long monitorId, ChromeTab tab, string message, bool allowRecoveryReload, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(95); var attempt = 0; var recoveryReloadUsed = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested(); attempt++;
            try
            {
                if (await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken)) { Activity?.Invoke(monitorId, $"Verified message accepted on attempt {attempt}."); return true; }
                Activity?.Invoke(monitorId, $"Verified composer delivery attempt {attempt} did not produce a user-message receipt.");
            }
            catch (Exception ex) when (IsTransientChromeException(ex)) { Activity?.Invoke(monitorId, $"Verified composer send retry {attempt}: {ex.GetType().Name}."); }

            if (allowRecoveryReload && !recoveryReloadUsed && DateTimeOffset.UtcNow < deadline)
            {
                recoveryReloadUsed = true;
                try
                {
                    Activity?.Invoke(monitorId, "Composer still unavailable. Reloading only the newly-created chat once before retrying delivery.");
                    await _chrome.ReloadTabAsync(tab, cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    await WaitForChatReadyAsync(monitorId, tab, cancellationToken);
                }
                catch (Exception ex) when (IsTransientChromeException(ex)) { Activity?.Invoke(monitorId, $"New-chat reload retry encountered a transient Chrome error: {ex.GetType().Name}."); }
            }

            await Task.Delay(1000, cancellationToken);
        }
        return false;
    }

    private async Task ApplyModelRouteAsync(SavedMonitor monitor, ChromeTab tab, bool recovery, bool contextRotation, CancellationToken cancellationToken)
    {
        if (!monitor.ModelRoutingEnabled) return; var decision = _modelRouting.Choose(monitor, recovery, contextRotation); if (string.Equals(decision.PreferredModel, "Auto", StringComparison.OrdinalIgnoreCase)) { Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Model routing: Auto; keeping ChatGPT's current model."); return; }
        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Selecting model '{decision.PreferredModel}' ({decision.Reason})..."); var selected = await _chrome.TrySelectModelAsync(tab, decision.PreferredModel, cancellationToken); if (selected) { await _database.AddLogAsync("System", decision.PreferredModel, string.Empty, "ModelSelected", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); return; }
        if (!string.Equals(decision.FallbackModel, decision.PreferredModel, StringComparison.OrdinalIgnoreCase) && !string.Equals(decision.FallbackModel, "Auto", StringComparison.OrdinalIgnoreCase)) { Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Preferred model '{decision.PreferredModel}' was not selectable. Trying configured fallback '{decision.FallbackModel}' once."); var fallbackSelected = await _chrome.TrySelectModelAsync(tab, decision.FallbackModel, cancellationToken); await _database.AddLogAsync("System", decision.FallbackModel, string.Empty, fallbackSelected ? "FallbackModelSelected" : "ModelSelectionSkipped", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); } else { await _database.AddLogAsync("System", decision.PreferredModel, string.Empty, "ModelSelectionSkipped", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); }
    }

    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)
    { Exception? last = null; for (var attempt = 1; attempt <= 3; attempt++) { try { return await _chrome.GetChatStateAsync(tab, cancellationToken); } catch (Exception ex) when (IsTransientChromeException(ex)) { last = ex; Activity?.Invoke(monitorId, $"Initial Chrome/CDP connection retry {attempt}/3: {ex.GetType().Name}"); await Task.Delay(500 * attempt, cancellationToken); } } throw last ?? new InvalidOperationException("Unable to read the ChatGPT tab state."); }

    private static bool IsTransientChromeException(Exception ex) => ex is WebSocketException || ex is TimeoutException || ex is TaskCanceledException || ex is IOException || ex.Message.Contains("Chrome closed the DevTools connection", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("connection was forcibly closed", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("unable to connect", StringComparison.OrdinalIgnoreCase);
    private static string GetEffectiveResponse(ChatPageState state) => !string.IsNullOrWhiteSpace(state.ErrorText) ? state.ErrorText.Trim() : state.LastAssistantText.Trim();
    private static bool IsDeliveryTimeout(string text) => text.Contains("message delivery timed out", StringComparison.OrdinalIgnoreCase);
    private static bool IsConversationContextLimit(string text) { if (string.IsNullOrWhiteSpace(text)) return false; string[] markers = { "conversation is too long", "conversation is too large", "context length", "context window", "maximum context", "conversation limit", "start a new chat", "this conversation has reached", "reached the maximum length", "المحادثة طويلة جدًا", "طول المحادثة", "حد المحادثة", "ابدأ محادثة جديدة" }; return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)); }
    private static bool IsErrorResponse(string text) { if (string.IsNullOrWhiteSpace(text)) return false; string[] markers = { "message delivery timed out", "something went wrong", "there was an error", "network error", "failed to generate", "error generating", "unable to generate", "unable to load", "حدث خطأ", "خطأ في الشبكة", "تعذر إنشاء", "تعذر تحميل" }; return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)); }
    private sealed record MonitorRuntime(CancellationTokenSource Cancellation, Task Worker);
}