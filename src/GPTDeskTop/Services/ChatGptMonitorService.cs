using System.Net.WebSockets;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChatGptMonitorService
{
    private static readonly TimeSpan RuntimeSettingsRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly MonitoringConfig _config;
    private readonly ModelRoutingService _modelRouting = new();
    private readonly object _sync = new();
    private readonly Dictionary<long, MonitorRuntime> _running = new();
    private readonly Dictionary<long, LifecycleGateEntry> _lifecycleGates = new();
    private readonly SemaphoreSlim _runtimeSettingsRefreshGate = new(1, 1);
    private RuntimeSettingsSnapshot? _runtimeSettingsSnapshot;

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
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            throw new InvalidOperationException("The saved monitor URL is not a stable ChatGPT conversation identity.");
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");
        if (!ChatGptConversationIdentity.IsSame(monitor.Url, tab.Url))
            throw new InvalidOperationException("The selected Chrome target no longer represents the saved ChatGPT conversation identity.");

        using var lifecycleLease = await AcquireLifecycleGateAsync(monitor.Id);
        var savedMonitors = await _database.GetSavedMonitorsAsync();
        var persistedMonitor = savedMonitors.FirstOrDefault(candidate => candidate.Id == monitor.Id);
        if (persistedMonitor is null)
        {
            Activity?.Invoke(monitor.Id, $"Monitor #{monitor.Id} no longer exists in SQLite. Stale Start was ignored.");
            return;
        }
        if (!ChatGptConversationIdentity.IsSame(persistedMonitor.Url, monitor.Url))
        {
            Activity?.Invoke(monitor.Id, $"Monitor #{monitor.Id} conversation identity changed before Start. Refresh the saved monitor before retrying.");
            return;
        }
        if (string.IsNullOrWhiteSpace(persistedMonitor.AutoReply))
            throw new InvalidOperationException("Auto reply text cannot be empty.");
        if (MonitorConversationOwnership.IsDuplicateOwner(monitor.Id, savedMonitors))
        {
            const string message = "Saved monitor conversation ownership is ambiguous. Resolve duplicate monitor rows before starting this monitor.";
            await _database.AddLogAsync(
                "System",
                string.Empty,
                message,
                "MonitorStartDuplicateConversationOwnership",
                persistedMonitor.Id,
                persistedMonitor.TabId,
                persistedMonitor.Title);
            HistoryChanged?.Invoke();
            Activity?.Invoke(persistedMonitor.Id, message);
            return;
        }

        ChromeTab? liveTab;
        try
        {
            var liveTabs = await _chrome.GetTabsAsync();
            liveTab = liveTabs.FirstOrDefault(candidate => string.Equals(candidate.Id, tab.Id, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is HttpRequestException || IsTransientChromeException(ex))
        {
            Activity?.Invoke(persistedMonitor.Id, $"Monitor #{persistedMonitor.Id}: live Chrome target revalidation is temporarily unavailable. Start was deferred: {ex.Message}");
            return;
        }

        if (liveTab is null)
        {
            Activity?.Invoke(persistedMonitor.Id, $"Monitor #{persistedMonitor.Id}: selected Chrome target disappeared before Start. Refresh the open conversations and retry.");
            return;
        }
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(liveTab.Url))
        {
            Activity?.Invoke(persistedMonitor.Id, $"Monitor #{persistedMonitor.Id}: selected Chrome target no longer exposes a stable ChatGPT conversation. Start was ignored.");
            return;
        }
        if (!ChatGptConversationIdentity.IsSame(persistedMonitor.Url, liveTab.Url))
        {
            Activity?.Invoke(persistedMonitor.Id, $"Monitor #{persistedMonitor.Id}: selected Chrome target navigated to a different conversation before Start. Refresh the open conversations and retry.");
            return;
        }

        var targetUpdated = await _database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
            persistedMonitor.Id,
            persistedMonitor.Url,
            liveTab.Id,
            liveTab.Title);
        if (!targetUpdated)
        {
            Activity?.Invoke(persistedMonitor.Id, $"Monitor #{persistedMonitor.Id}: saved conversation changed before the live Chrome target could be committed. Start was ignored.");
            return;
        }
        persistedMonitor.TabId = liveTab.Id;
        persistedMonitor.Title = liveTab.Title;

        lock (_sync)
        {
            if (_running.ContainsKey(persistedMonitor.Id)) return;
            var cts = new CancellationTokenSource();
            var worker = Task.Run(() => MonitorLoopAsync(persistedMonitor, liveTab, cts.Token));
            _running.Add(persistedMonitor.Id, new MonitorRuntime(cts, worker));
        }
        Activity?.Invoke(persistedMonitor.Id, $"Started: {persistedMonitor.Title}");
        RunningStateChanged?.Invoke();
    }

    public async Task<bool> UpdateMonitorConfigurationAsync(SavedMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before changing its settings.");
        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");

        using var lifecycleLease = await AcquireLifecycleGateAsync(monitor.Id);
        lock (_sync)
        {
            if (_running.ContainsKey(monitor.Id)) return false;
        }

        return await _database.UpdateMonitorConfigurationAsync(monitor);
    }

    public async Task StopMonitorAsync(long monitorId)
    {
        using var lifecycleLease = await AcquireLifecycleGateAsync(monitorId);
        await StopMonitorCoreAsync(monitorId);
    }

    public async Task DeleteMonitorAsync(long monitorId)
    {
        using var lifecycleLease = await AcquireLifecycleGateAsync(monitorId);
        await StopMonitorCoreAsync(monitorId);
        await _database.DeleteMonitorAsync(monitorId);
    }

    private async Task StopMonitorCoreAsync(long monitorId)
    {
        MonitorRuntime? runtime;
        lock (_sync)
        {
            if (!_running.TryGetValue(monitorId, out runtime)) return;
            runtime.StopOwnsCleanup = true;
        }

        runtime.Cancellation.Cancel();
        try
        {
            await runtime.Worker;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            var removed = false;
            lock (_sync)
            {
                if (_running.TryGetValue(monitorId, out var current) && ReferenceEquals(current, runtime))
                {
                    _running.Remove(monitorId);
                    removed = true;
                }
            }
            runtime.Cancellation.Dispose();
            Activity?.Invoke(monitorId, "Stopped.");
            if (removed) RunningStateChanged?.Invoke();
        }
    }

    public async Task StopAllAsync() { long[] ids; lock (_sync) ids = _running.Keys.ToArray(); await Task.WhenAll(ids.Select(StopMonitorAsync)); }

    private async Task<LifecycleGateLease> AcquireLifecycleGateAsync(long monitorId)
    {
        LifecycleGateEntry entry;
        lock (_sync)
        {
            if (!_lifecycleGates.TryGetValue(monitorId, out entry!))
            {
                entry = new LifecycleGateEntry();
                _lifecycleGates.Add(monitorId, entry);
            }
            entry.ReferenceCount++;
        }

        try
        {
            await entry.Gate.WaitAsync();
            return new LifecycleGateLease(this, monitorId, entry);
        }
        catch
        {
            ReleaseLifecycleGateReference(monitorId, entry);
            throw;
        }
    }

    private void ReleaseLifecycleGate(long monitorId, LifecycleGateEntry entry)
    {
        entry.Gate.Release();
        ReleaseLifecycleGateReference(monitorId, entry);
    }

    private void ReleaseLifecycleGateReference(long monitorId, LifecycleGateEntry entry)
    {
        SemaphoreSlim? gateToDispose = null;
        lock (_sync)
        {
            if (entry.ReferenceCount <= 0)
                throw new InvalidOperationException("Lifecycle gate reference count underflow.");

            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0
                && _lifecycleGates.TryGetValue(monitorId, out var current)
                && ReferenceEquals(current, entry))
            {
                _lifecycleGates.Remove(monitorId);
                gateToDispose = entry.Gate;
            }
        }

        gateToDispose?.Dispose();
    }

    private async Task<RuntimeSettingsSnapshot> GetRuntimeSettingsSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = Volatile.Read(ref _runtimeSettingsSnapshot);
        if (cached is not null && now < cached.ExpiresUtc)
            return cached;

        await _runtimeSettingsRefreshGate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            cached = Volatile.Read(ref _runtimeSettingsSnapshot);
            if (cached is not null && now < cached.ExpiresUtc)
                return cached;

            var rotateTask = _database.GetIntSettingAsync(
                "RotateAfterAssistantMessages", 0, 0, 10000, cancellationToken);
            var messageTask = _database.GetSettingAsync(
                "MessageCountRotationStartMessage", cancellationToken);
            await Task.WhenAll(rotateTask, messageTask);

            var snapshot = new RuntimeSettingsSnapshot(
                await rotateTask,
                (await messageTask) ?? "كمل",
                DateTimeOffset.UtcNow + RuntimeSettingsRefreshInterval);
            Volatile.Write(ref _runtimeSettingsSnapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _runtimeSettingsRefreshGate.Release();
        }
    }

    private async Task MonitorLoopAsync(SavedMonitor monitor, ChromeTab tab, CancellationToken cancellationToken)
    {
        var timerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        var replyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        var runtimeSettings = await GetRuntimeSettingsSnapshotAsync(cancellationToken);
        var rotateAfterMessages = runtimeSettings.RotateAfterMessages;
        var messageCountRotationStartMessage = runtimeSettings.MessageCountRotationStartMessage;
        var nextRuntimeSettingsRefreshUtc = runtimeSettings.ExpiresUtc;
        var transientFailures = 0;
        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Timer {timerSeconds}s | Delay {replyDelaySeconds}s | Passive long-response wait ON (elapsed time never reloads a healthy chat) | Rotation {(monitor.ConversationRotationEnabled ? "ON" : "OFF")} | Count rotation {(rotateAfterMessages > 0 ? $"{rotateAfterMessages} assistant messages" : "OFF")} | Model routing {(monitor.ModelRoutingEnabled ? "ON" : "OFF")} | Reply: {monitor.AutoReply}");
        try
        {
            var initial = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);
            var initialText = GetEffectiveResponse(initial);
            var initialCountRotationDue = monitor.ConversationRotationEnabled
                && rotateAfterMessages > 0
                && !initial.IsGenerating
                && !string.IsNullOrWhiteSpace(initialText)
                && string.IsNullOrWhiteSpace(initial.ErrorText)
                && !IsConversationContextLimit(initialText)
                && initial.AssistantCount >= rotateAfterMessages;
            var lastHandledText = initialCountRotationDue ? string.Empty : initialText;
            var candidateText = string.Empty;
            var candidateSince = DateTimeOffset.MinValue;
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
                    if (DateTimeOffset.UtcNow >= nextRuntimeSettingsRefreshUtc)
                    {
                        runtimeSettings = await GetRuntimeSettingsSnapshotAsync(cancellationToken);
                        rotateAfterMessages = runtimeSettings.RotateAfterMessages;
                        messageCountRotationStartMessage = runtimeSettings.MessageCountRotationStartMessage;
                        nextRuntimeSettingsRefreshUtc = runtimeSettings.ExpiresUtc;
                    }
                    var messageCountThresholdReached = monitor.ConversationRotationEnabled
                        && rotateAfterMessages > 0
                        && !state.IsGenerating
                        && !string.IsNullOrWhiteSpace(text)
                        && string.IsNullOrWhiteSpace(state.ErrorText)
                        && !IsConversationContextLimit(text)
                        && state.AssistantCount >= rotateAfterMessages;
                    var rotationSlotAvailable = monitor.MaxConversationRotations <= 0 || monitor.RotationCount < monitor.MaxConversationRotations;
                    var messageCountRotationDue = messageCountThresholdReached && rotationSlotAvailable;

                    // A slow/unchanged/empty response is a passive wait state. Time elapsed by itself
                    // must never mutate the page. Recovery is driven only by explicit current ChatGPT
                    // error UI or explicit terminal conditions such as conversation/context limits.
                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || (string.Equals(text, lastHandledText, StringComparison.Ordinal) && !messageCountRotationDue)) { candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; continue; }
                    if (!string.Equals(candidateText, text, StringComparison.Ordinal)) { candidateText = text; candidateSince = DateTimeOffset.UtcNow; Activity?.Invoke(monitor.Id, $"{prefix} New response detected..."); continue; }
                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;
                    lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText); await _database.AddLogAsync("Inbound", string.Empty, text, IsConversationContextLimit(text) ? "ConversationLimit" : isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);

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
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }

                        tab = newTab; lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
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
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        var committedTab = await CommitVerifiedConversationHandoffAsync(
                            monitor, oldTab, newTab, startMessage, text,
                            rotationTrigger: "ConversationContextLimit",
                            successStatus: "RotatedToNewChat",
                            outboundStatus: "RotationStartSent",
                            conflictStatus: "RotationHandoffCommitDeferred",
                            incrementRotationCount: true,
                            recordRotation: true,
                            cancellationToken);
                        if (committedTab is null)
                        {
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        tab = committedTab; lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"{prefix} Rotation #{monitor.RotationCount} complete. Monitoring the new ChatGPT conversation under the same Monitor ID.");
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
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        var committedRecoveryTab = await CommitVerifiedConversationHandoffAsync(
                            monitor, oldTab, newTab, recoveryMessage, text,
                            rotationTrigger: "DeliveryTimeout",
                            successStatus: "RecoveredToNewChat",
                            outboundStatus: "RecoverySent",
                            conflictStatus: "RecoveryHandoffCommitDeferred",
                            incrementRotationCount: false,
                            recordRotation: false,
                            cancellationToken);
                        if (committedRecoveryTab is null)
                        {
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }
                        tab = committedRecoveryTab; lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;
                    }
                    if (isError)
                    { Activity?.Invoke(monitor.Id, $"{prefix} Error saved. Refreshing only this tab..."); try { await _chrome.ReloadTabAsync(tab, cancellationToken); await _database.AddLogAsync("System", "Page.reload", text, "RefreshedAfterError", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); await Task.Delay(Math.Max(1500, _config.DelayAfterSendMilliseconds), cancellationToken); } catch (Exception refreshEx) when (refreshEx is not OperationCanceledException && !IsTransientChromeException(refreshEx)) { ExceptionLogService.Log(refreshEx, "Monitor.RefreshAfterError", monitor.Id, tab.Id, monitor.Title); await _database.AddLogAsync("System", "Page.reload", refreshEx.ToString(), "RefreshFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); } continue; }
                    if (replyDelaySeconds > 0)
                    { Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before auto reply..."); await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken); var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken); var latestText = GetEffectiveResponse(recheck); if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal)) { await _database.AddLogAsync("System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); continue; } }
                    var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply, allowRecoveryReload: false, cancellationToken); await _database.AddLogAsync("Outbound", monitor.AutoReply, string.Empty, autoSent ? "Sent" : "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (IsTransientChromeException(ex)) { transientFailures++; if (transientFailures <= 3) Activity?.Invoke(monitor.Id, $"CDP transient disconnect ({transientFailures}/3): {ex.GetType().Name}. Retrying..."); else if (transientFailures == 4) Activity?.Invoke(monitor.Id, "CDP is temporarily unavailable. Background retry continues; this is not counted as an application crash."); await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 750 * transientFailures)), cancellationToken); }
                catch (Exception ex) { Activity?.Invoke(monitor.Id, $"Monitor exception logged: {ex.GetType().Name}: {ex.Message}"); await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.MonitorLoop", monitor.Id, tab.Id, monitor.Title); HistoryChanged?.Invoke(); await Task.Delay(1500, cancellationToken); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Activity?.Invoke(monitor.Id, "Monitor cancellation requested; stopping cleanly."); }
        catch (Exception ex) when (IsTransientChromeException(ex)) { Activity?.Invoke(monitor.Id, $"Chrome connection unavailable during startup. Monitor remains stopped and can be started again: {ex.Message}"); }
        catch (Exception ex) { await ExceptionLogService.LogAsync(ex, "ChatGptMonitorService.WorkerFatal", monitor.Id, tab.Id, monitor.Title); Activity?.Invoke(monitor.Id, $"Monitor worker stopped by exception: {ex.Message}"); }
        finally
        {
            MonitorRuntime? runtimeToDispose = null;
            lock (_sync)
            {
                if (_running.TryGetValue(monitor.Id, out var current)
                    && current.Cancellation.Token == cancellationToken
                    && !current.StopOwnsCleanup)
                {
                    _running.Remove(monitor.Id);
                    runtimeToDispose = current;
                }
            }

            if (runtimeToDispose is not null)
            {
                runtimeToDispose.Cancellation.Dispose();
                RunningStateChanged?.Invoke();
            }
        }
    }

    private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync(
        SavedMonitor monitor,
        ChromeTab oldTab,
        ChromeTab openedTab,
        string startMessage,
        string triggerResponse,
        string rotationTrigger,
        string successStatus,
        string outboundStatus,
        string conflictStatus,
        bool incrementRotationCount,
        bool recordRotation,
        CancellationToken cancellationToken)
    {
        var stableTab = await ResolveStableCreatedConversationAsync(monitor.Id, openedTab, cancellationToken);
        if (stableTab is null)
        {
            await _database.AddLogAsync(
                "System",
                startMessage,
                "Verified delivery succeeded, but Chrome did not expose a stable /c/{conversation-id} URL for the new target. The new tab was not claimed.",
                conflictStatus,
                monitor.Id,
                openedTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(openedTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff tab close failed transiently: {closeEx.Message}"); }
            return null;
        }

        if (ChatGptConversationIdentity.IsSame(monitor.Url, stableTab.Url))
        {
            await _database.AddLogAsync(
                "System",
                startMessage,
                "The new handoff target resolved back to the current saved conversation. The target was not claimed.",
                conflictStatus,
                monitor.Id,
                stableTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(stableTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed same-conversation tab close failed transiently: {closeEx.Message}"); }
            return null;
        }

        var expectedUrl = monitor.Url;
        try
        {
            var committed = await _database.CommitMonitorConversationHandoffAsync(
                monitor.Id,
                expectedUrl,
                stableTab.Id,
                stableTab.Title,
                stableTab.Url,
                incrementRotationCount,
                recordRotation,
                oldTab.Id,
                rotationTrigger,
                startMessage,
                triggerResponse,
                successStatus,
                outboundStatus,
                cancellationToken);

            stableTab.Title = committed.Title;
            monitor.TabId = stableTab.Id;
            monitor.Title = committed.Title;
            monitor.Url = committed.NewUrl;
            monitor.RotationCount = committed.RotationCount;
            HistoryChanged?.Invoke();
            return stableTab;
        }
        catch (InvalidOperationException ex)
        {
            Activity?.Invoke(monitor.Id, $"Intentional conversation handoff was not committed: {ex.Message}");
            await _database.AddLogAsync(
                "System",
                startMessage,
                ex.Message,
                conflictStatus,
                monitor.Id,
                stableTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(stableTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff conflict tab close failed transiently: {closeEx.Message}"); }
            return null;
        }
    }

    private async Task<ChromeTab?> ResolveStableCreatedConversationAsync(long monitorId, ChromeTab openedTab, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await _chrome.GetTabsAsync(cancellationToken);
                var current = tabs.FirstOrDefault(tab => string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal));
                if (current is not null && RuntimeHealthPresentation.IsChatGptConversationUrl(current.Url))
                {
                    Activity?.Invoke(monitorId, $"[{current.Title}] Stable conversation identity resolved after verified new-chat delivery.");
                    return current;
                }
            }
            catch (Exception ex) when (IsTransientChromeException(ex))
            {
                Activity?.Invoke(monitorId, $"Waiting for stable new-chat conversation identity: {ex.GetType().Name}.");
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
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

        var committedTab = await CommitVerifiedConversationHandoffAsync(
            monitor, oldTab, newTab, startMessage, triggerText,
            rotationTrigger: "AssistantMessageCount",
            successStatus: "RotatedByMessageCount",
            outboundStatus: "MessageCountRotationStartSent",
            conflictStatus: "MessageCountRotationCommitDeferred",
            incrementRotationCount: true,
            recordRotation: true,
            cancellationToken);
        if (committedTab is null)
            return null;

        Activity?.Invoke(monitor.Id, $"{prefix} Message-count rotation #{monitor.RotationCount} complete. Same Monitor ID is now bound to the new conversation.");
        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after message-count rotation: {closeEx.Message}"); }
        if (monitor.RotationCooldownSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken);

        return committedTab;
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

    private sealed record RuntimeSettingsSnapshot(
        int RotateAfterMessages,
        string MessageCountRotationStartMessage,
        DateTimeOffset ExpiresUtc);

    private sealed class LifecycleGateEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class LifecycleGateLease : IDisposable
    {
        private readonly ChatGptMonitorService _owner;
        private readonly long _monitorId;
        private readonly LifecycleGateEntry _entry;
        private int _disposed;

        public LifecycleGateLease(ChatGptMonitorService owner, long monitorId, LifecycleGateEntry entry)
        {
            _owner = owner;
            _monitorId = monitorId;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.ReleaseLifecycleGate(_monitorId, _entry);
        }
    }

    private sealed class MonitorRuntime
    {
        public MonitorRuntime(CancellationTokenSource cancellation, Task worker)
        {
            Cancellation = cancellation;
            Worker = worker;
        }

        public CancellationTokenSource Cancellation { get; }
        public Task Worker { get; }
        public bool StopOwnsCleanup { get; set; }
    }
}
