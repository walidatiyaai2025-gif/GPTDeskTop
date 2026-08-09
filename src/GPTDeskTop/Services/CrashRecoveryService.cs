using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public enum CrashRecoveryMode
{
    FreshCrashReset,
    PendingRetry
}

public static class CrashRecoveryService
{
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);

    public static Task RecoverIfPendingAsync(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
        => RecoverIfPendingAsync(
            new CrashRecoveryRuntimeAdapter(chrome, monitorService),
            database,
            CrashRecoveryMode.FreshCrashReset,
            cancellationToken);

    public static Task RecoverIfPendingAsync(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CrashRecoveryMode mode,
        CancellationToken cancellationToken = default)
        => RecoverIfPendingAsync(
            new CrashRecoveryRuntimeAdapter(chrome, monitorService),
            database,
            mode,
            cancellationToken);

    public static Task RecoverIfPendingAsync(
        ICrashRecoveryRuntime runtime,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
        => RecoverIfPendingAsync(
            runtime,
            database,
            CrashRecoveryMode.FreshCrashReset,
            cancellationToken);

    public static async Task RecoverIfPendingAsync(
        ICrashRecoveryRuntime runtime,
        LocalDatabase database,
        CrashRecoveryMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(database);

        await RecoveryGate.WaitAsync(cancellationToken);
        try
        {
            await RecoverCoreAsync(runtime, database, mode, cancellationToken);
        }
        finally
        {
            RecoveryGate.Release();
        }
    }

    private static async Task RecoverCoreAsync(
        ICrashRecoveryRuntime runtime,
        LocalDatabase database,
        CrashRecoveryMode mode,
        CancellationToken cancellationToken)
    {
        var pending = await database.GetSettingAsync("CrashRecoveryPending", cancellationToken);
        if (!string.Equals(pending, "1", StringComparison.Ordinal))
            return;

        var monitors = await database.GetSavedMonitorsAsync(cancellationToken);
        if (monitors.Count == 0)
        {
            await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
            return;
        }

        var duplicateMonitorIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);
        var recoveryId = await database.GetSettingAsync("CrashRecovery.RecoveryId", cancellationToken);
        if (string.IsNullOrWhiteSpace(recoveryId))
        {
            recoveryId = Guid.NewGuid().ToString("N");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", recoveryId, cancellationToken);
        }

        var recoveryMessage = await database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل";
        if (string.IsNullOrWhiteSpace(recoveryMessage)) recoveryMessage = "كمل";

        try
        {
            var validMonitors = monitors
                .Where(saved => RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)
                                && !duplicateMonitorIds.Contains(saved.Id))
                .ToList();

            var availableTabs = new List<ChromeTab>();
            ChromeTab? firstTab = null;
            SavedMonitor? firstValidMonitor = validMonitors.FirstOrDefault();

            if (mode == CrashRecoveryMode.FreshCrashReset)
            {
                await runtime.StopAllMonitorsAsync();
                await runtime.CloseAllMonitorTabsAsync(cancellationToken);

                if (firstValidMonitor is not null)
                {
                    await runtime.DelayAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
                    runtime.LaunchMonitorChrome(firstValidMonitor.Url);
                    await runtime.DelayAsync(TimeSpan.FromMilliseconds(2200), cancellationToken);

                    var currentTabs = await runtime.GetTabsAsync(cancellationToken);
                    availableTabs.AddRange(currentTabs);
                    firstTab = currentTabs.FirstOrDefault(t =>
                        ChatGptConversationIdentity.IsSame(t.Url, firstValidMonitor.Url));

                    if (firstTab is null)
                    {
                        firstTab = await runtime.CreateTabAsync(firstValidMonitor.Url, cancellationToken);
                        availableTabs.Add(firstTab);
                    }
                }
            }
            else if (firstValidMonitor is not null)
            {
                var currentTabs = await runtime.GetTabsAsync(cancellationToken);
                availableTabs.AddRange(currentTabs.Where(tab =>
                    RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)));
            }

            var outcomes = new List<CrashRecoveryOutcome>(monitors.Count);
            var usedTabIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in monitors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (duplicateMonitorIds.Contains(saved.Id))
                {
                    outcomes.Add(CrashRecoveryOutcome.DuplicateConversationOwnership);
                    await database.AddLogAsync(
                        "System",
                        "CrashRecovery",
                        "Saved monitor conversation ownership is ambiguous. Resolve duplicate monitor rows before recovery can act on this conversation.",
                        "CrashRecoveryDuplicateConversationOwnership",
                        saved.Id,
                        saved.TabId,
                        saved.Title,
                        cancellationToken);
                    continue;
                }

                if (!RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url))
                {
                    outcomes.Add(CrashRecoveryOutcome.InvalidConversationIdentity);
                    await database.AddLogAsync(
                        "System",
                        "CrashRecovery",
                        "Saved monitor URL is not a stable ChatGPT conversation identity. Re-add this monitor from an open conversation before recovery can complete.",
                        "CrashRecoveryInvalidConversationIdentity",
                        saved.Id,
                        saved.TabId,
                        saved.Title,
                        cancellationToken);
                    continue;
                }

                var successKey = $"CrashRecovery.{recoveryId}.Monitor.{saved.Id}.Success";
                var alreadyRecovered = string.Equals(
                    await database.GetSettingAsync(successKey, cancellationToken),
                    "1",
                    StringComparison.Ordinal);

                ChromeTab? tab = null;
                if (mode == CrashRecoveryMode.FreshCrashReset
                    && firstValidMonitor is not null
                    && saved.Id == firstValidMonitor.Id
                    && firstTab is not null)
                {
                    tab = firstTab;
                }
                else if (mode == CrashRecoveryMode.PendingRetry)
                {
                    tab = ResolveReusableTab(saved, availableTabs, usedTabIds);
                }

                if (tab is null)
                {
                    tab = await runtime.CreateTabAsync(saved.Url, cancellationToken);
                    availableTabs.Add(tab);
                }

                if (!ChatGptConversationIdentity.IsSame(tab.Url, saved.Url))
                {
                    outcomes.Add(CrashRecoveryOutcome.SendFailed);
                    await database.AddLogAsync(
                        "System",
                        "CrashRecovery",
                        "Chrome did not return the saved stable ChatGPT conversation identity for this monitor.",
                        "CrashRecoveryTabIdentityMismatch",
                        saved.Id,
                        tab.Id,
                        saved.Title,
                        cancellationToken);
                    continue;
                }

                var resolvedTitle = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;
                var targetUpdated = await database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
                    saved.Id,
                    saved.Url,
                    tab.Id,
                    resolvedTitle,
                    cancellationToken);
                if (!targetUpdated)
                {
                    outcomes.Add(CrashRecoveryOutcome.SendFailed);
                    await database.AddLogAsync(
                        "System",
                        "CrashRecovery",
                        "Saved monitor conversation identity changed while recovery was resolving its Chrome target. Recovery skipped this stale snapshot.",
                        "CrashRecoverySavedConversationChanged",
                        saved.Id,
                        tab.Id,
                        saved.Title,
                        cancellationToken);
                    continue;
                }

                saved.TabId = tab.Id;
                saved.Title = resolvedTitle;
                if (!string.IsNullOrWhiteSpace(tab.Id))
                    usedTabIds.Add(tab.Id);

                if (alreadyRecovered)
                {
                    outcomes.Add(CrashRecoveryOutcome.Success);
                    await database.AddLogAsync(
                        "System",
                        recoveryMessage,
                        string.Empty,
                        mode == CrashRecoveryMode.PendingRetry
                            ? "CrashRecoveryAlreadyVerifiedReused"
                            : "CrashRecoveryAlreadyVerified",
                        saved.Id,
                        tab.Id,
                        saved.Title,
                        cancellationToken);
                    if (saved.Enabled)
                        await runtime.StartMonitorAsync(saved, tab);
                    continue;
                }

                var sent = await SendWithRetryAsync(runtime, tab, recoveryMessage, cancellationToken);
                var outcome = sent ? CrashRecoveryOutcome.Success : CrashRecoveryOutcome.SendFailed;
                outcomes.Add(outcome);

                await database.AddLogAsync(
                    "System",
                    recoveryMessage,
                    string.Empty,
                    sent ? "CrashRecoverySent" : "CrashRecoverySendFailed",
                    saved.Id,
                    tab.Id,
                    saved.Title,
                    cancellationToken);

                if (sent)
                    await database.SetSettingAsync(successKey, "1", cancellationToken);

                if (CrashRecoveryOutcomePolicy.ShouldStartMonitor(outcome, saved.Enabled))
                    await runtime.StartMonitorAsync(saved, tab);
            }

            if (CrashRecoveryOutcomePolicy.ShouldClearPending(outcomes))
            {
                await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
                await database.SetSettingAsync("CrashRecovery.RecoveryId", string.Empty, cancellationToken);
                await database.AddLogAsync("System", "CrashRecovery", string.Empty, "CrashRecoveryCompleted", cancellationToken: cancellationToken);
            }
            else
            {
                await database.AddLogAsync(
                    "System",
                    "CrashRecovery",
                    mode == CrashRecoveryMode.PendingRetry ? "PendingRetry" : string.Empty,
                    "CrashRecoveryPartialFailure",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionLogService.Log(ex, "CrashRecoveryService.RecoverIfPendingAsync");
            await database.AddLogAsync("System", "CrashRecovery", ex.ToString(), "CrashRecoveryFailed", cancellationToken: cancellationToken);
        }
    }

    private static ChromeTab? ResolveReusableTab(
        SavedMonitor monitor,
        IReadOnlyCollection<ChromeTab> tabs,
        ISet<string> usedTabIds)
    {
        if (!string.IsNullOrWhiteSpace(monitor.TabId))
        {
            var exactTarget = tabs.FirstOrDefault(tab =>
                !usedTabIds.Contains(tab.Id)
                && string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal)
                && ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));
            if (exactTarget is not null)
                return exactTarget;
        }

        return tabs.FirstOrDefault(tab =>
            !usedTabIds.Contains(tab.Id)
            && ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));
    }

    private static async Task<bool> SendWithRetryAsync(
        ICrashRecoveryRuntime runtime,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await runtime.DelayAsync(
                attempt == 1 ? TimeSpan.FromMilliseconds(1200) : TimeSpan.FromMilliseconds(700),
                cancellationToken);
            try
            {
                if (await runtime.SendChatMessageVerifiedAsync(tab, message, cancellationToken))
                    return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase))
            {
            }
        }
        return false;
    }
}