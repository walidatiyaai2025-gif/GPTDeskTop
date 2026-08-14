using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record NewChatMonitorWorkflowResult(SavedMonitor Monitor, ChromeTab ConversationTab);

public sealed class NewChatMonitorWorkflowService
{
    private sealed record FreshChatContext(ChromeTab OpenedTab, HashSet<string> PreexistingTargetIds);

    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;

    public NewChatMonitorWorkflowService(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;
    }

    public async Task<NewChatMonitorWorkflowResult> ExecuteAsync(
        string initialChatMessage,
        string monitorAutoReply,
        CancellationToken cancellationToken = default)
    {
        initialChatMessage = RequireMessage(initialChatMessage, "Initial Chat Message");
        monitorAutoReply = RequireMessage(monitorAutoReply, "Monitor Auto Reply");

        var restoreHiddenChrome = string.Equals(
            await _database.GetSettingAsync("ChromeHidden", cancellationToken).ConfigureAwait(false),
            "1",
            StringComparison.Ordinal);

        ChromeTab? openedTab = null;
        try
        {
            var freshChat = await CreateFreshChatTabAsync(cancellationToken).ConfigureAwait(false);
            openedTab = freshChat.OpenedTab;
            var sent = await SendInitialMessageVerifiedAsync(openedTab, initialChatMessage, cancellationToken).ConfigureAwait(false);

            // The first send normally navigates ChatGPT from the new-chat shell to /c/{conversation-id}.
            // ChatGPT can also replace the CDP target during that transition. Resolve either the original
            // target after it becomes stable or the one unambiguous stable target that appeared after this
            // workflow started. The baseline target set prevents attaching to an older conversation.
            var stableTab = await ResolveStableConversationAsync(
                openedTab,
                freshChat.PreexistingTargetIds,
                cancellationToken).ConfigureAwait(false);
            if (!sent && stableTab is not null)
            {
                sent = await _chrome.SendChatMessageVerifiedAsync(
                    stableTab,
                    initialChatMessage,
                    cancellationToken,
                    requireNewTurn: false).ConfigureAwait(false);
            }

            if (!sent)
                throw new InvalidOperationException("The initial ChatGPT message could not be verified after stable-conversation recovery. The new tab was kept open for inspection and no duplicate monitor was created.");

            stableTab ??= await ResolveStableConversationAsync(
                openedTab,
                freshChat.PreexistingTargetIds,
                cancellationToken).ConfigureAwait(false);
            if (stableTab is null)
                throw new InvalidOperationException("The new ChatGPT target did not expose a stable conversation URL after verified delivery. No monitor was created.");

            var savedMonitor = await BuildMonitorAsync(stableTab, monitorAutoReply, cancellationToken).ConfigureAwait(false);
            var registration = await _database.RegisterMonitorIfConversationAvailableAsync(savedMonitor, cancellationToken).ConfigureAwait(false);
            if (!registration.Created)
                throw new InvalidOperationException($"The new conversation is already owned by saved monitor #{registration.MonitorId}. A second monitor was not created.");

            savedMonitor.Id = registration.MonitorId;
            await _database.AddLogAsync(
                "Outbound",
                initialChatMessage,
                string.Empty,
                "NewChatBootstrapSent",
                savedMonitor.Id,
                stableTab.Id,
                stableTab.Title,
                cancellationToken).ConfigureAwait(false);

            await _monitor.StartMonitorAsync(savedMonitor, stableTab).ConfigureAwait(false);
            if (!_monitor.IsMonitorRunning(savedMonitor.Id))
                throw new InvalidOperationException($"Monitor #{savedMonitor.Id} was saved but could not be started on the verified new conversation.");

            await LastWorkingStateService.SetMonitorDesiredRunningAsync(
                _database,
                savedMonitor.Id,
                true,
                cancellationToken).ConfigureAwait(false);

            return new NewChatMonitorWorkflowResult(savedMonitor, stableTab);
        }
        finally
        {
            if (restoreHiddenChrome)
            {
                try { await _chrome.HideMonitorChromeAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ExceptionLogService.Log(ex, "NewChatMonitorWorkflow.RestoreChromeVisibility");
                }
            }
        }
    }

    private async Task<FreshChatContext> CreateFreshChatTabAsync(CancellationToken cancellationToken)
    {
        try
        {
            var existingTabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            if (existingTabs.Count > 0)
            {
                var baseline = existingTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
                var opened = await _chrome.CreateNewChatTabAsync(cancellationToken).ConfigureAwait(false);
                return new FreshChatContext(opened, baseline);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ChromeTransportFailureClassifier.IsTransient(ex))
        {
            // A stale DevTools endpoint/session is expected to recover below after the dedicated browser is ensured.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionLogService.Log(ex, "NewChatMonitorWorkflow.ReadChromeBeforeLaunch");
        }

        _chrome.LaunchMonitorChrome();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                if (tabs.Count > 0)
                {
                    var baseline = tabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
                    var opened = await _chrome.CreateNewChatTabAsync(cancellationToken).ConfigureAwait(false);
                    return new FreshChatContext(opened, baseline);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Monitor Chrome did not become ready for a new ChatGPT conversation within 30 seconds.{(lastError is null ? string.Empty : $" Last error: {lastError.Message}")}");
    }

    private async Task<bool> SendInitialMessageVerifiedAsync(
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken).ConfigureAwait(false))
                    return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ChromeTransportFailureClassifier.IsTransient(ex))
            {
                // Verified send retires broken sessions and re-checks the DOM before a resend.
                // Treat remaining transient transport failures as recovery state, not crash diagnostics.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, $"NewChatMonitorWorkflow.InitialSendAttempt{attempt}");
            }

            if (attempt == 1)
            {
                try
                {
                    await _chrome.ReloadTabAsync(tab, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ChromeTransportFailureClassifier.IsTransient(ex))
                {
                    // Reload may race a navigation or a retired target session. The next verified-send attempt rebinds it.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ExceptionLogService.Log(ex, "NewChatMonitorWorkflow.InitialSendReload");
                }
            }
            else if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task<ChromeTab?> ResolveStableConversationAsync(
        ChromeTab openedTab,
        IReadOnlySet<string> preexistingTargetIds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                var stable = NewChatStableTargetSelector.Select(openedTab, preexistingTargetIds, tabs);
                if (stable is not null)
                    return stable;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ChromeTransportFailureClassifier.IsTransient(ex))
            {
                // Navigation/CDP churn while the new chat receives its /c/{id} identity is recoverable.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "NewChatMonitorWorkflow.ResolveStableConversation");
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<SavedMonitor> BuildMonitorAsync(
        ChromeTab stableTab,
        string monitorAutoReply,
        CancellationToken cancellationToken)
    {
        var defaultDelay = await _database.GetIntSettingAsync("DefaultMonitorDelaySeconds", 3, 0, 300, cancellationToken).ConfigureAwait(false);
        var defaultTimer = await _database.GetIntSettingAsync("DefaultMonitorTimerSeconds", 1, 1, 60, cancellationToken).ConfigureAwait(false);
        var rotationEnabled = string.Equals(await _database.GetSettingAsync("DefaultConversationRotationEnabled", cancellationToken).ConfigureAwait(false), "1", StringComparison.Ordinal);
        var newChatStartMessage = await _database.GetSettingAsync("DefaultNewChatStartMessage", cancellationToken).ConfigureAwait(false) ?? "كمل";
        var newChatDelay = await _database.GetIntSettingAsync("DefaultNewChatDelaySeconds", 30, 0, 600, cancellationToken).ConfigureAwait(false);
        var rotationCooldown = await _database.GetIntSettingAsync("DefaultRotationCooldownSeconds", 60, 0, 3600, cancellationToken).ConfigureAwait(false);
        var maxRotations = await _database.GetIntSettingAsync("DefaultMaxConversationRotations", 0, 0, 1000, cancellationToken).ConfigureAwait(false);
        var modelRoutingEnabled = string.Equals(await _database.GetSettingAsync("DefaultModelRoutingEnabled", cancellationToken).ConfigureAwait(false), "1", StringComparison.Ordinal);
        var preferredModel = await _database.GetSettingAsync("DefaultPreferredModel", cancellationToken).ConfigureAwait(false) ?? "Auto";
        var fallbackModel = await _database.GetSettingAsync("DefaultFallbackModel", cancellationToken).ConfigureAwait(false) ?? preferredModel;

        return new SavedMonitor
        {
            TabId = stableTab.Id,
            Title = string.IsNullOrWhiteSpace(stableTab.Title) ? "ChatGPT conversation" : stableTab.Title,
            Url = stableTab.Url,
            AutoReply = monitorAutoReply,
            ReplyDelaySeconds = defaultDelay,
            TimerSeconds = defaultTimer,
            Enabled = true,
            ConversationRotationEnabled = rotationEnabled,
            NewChatStartMessage = string.IsNullOrWhiteSpace(newChatStartMessage) ? "كمل" : newChatStartMessage,
            NewChatDelaySeconds = newChatDelay,
            RotationCooldownSeconds = rotationCooldown,
            MaxConversationRotations = maxRotations,
            ModelRoutingEnabled = modelRoutingEnabled,
            PreferredModel = string.IsNullOrWhiteSpace(preferredModel) ? "Auto" : preferredModel,
            FallbackModel = string.IsNullOrWhiteSpace(fallbackModel) ? "Auto" : fallbackModel
        };
    }

    private static string RequireMessage(string value, string fieldName)
    {
        var message = value?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw new ArgumentException($"{fieldName} cannot be empty.", fieldName);
        return message;
    }
}
