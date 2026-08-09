using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class CrashRecoveryService
{
    public static Task RecoverIfPendingAsync(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
        => RecoverIfPendingAsync(
            new CrashRecoveryRuntimeAdapter(chrome, monitorService),
            database,
            cancellationToken);

    public static async Task RecoverIfPendingAsync(
        ICrashRecoveryRuntime runtime,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(database);

        var pending = await database.GetSettingAsync("CrashRecoveryPending", cancellationToken);
        if (!string.Equals(pending, "1", StringComparison.Ordinal))
            return;

        var monitors = await database.GetSavedMonitorsAsync(cancellationToken);
        if (monitors.Count == 0)
        {
            await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
            return;
        }

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
            await runtime.StopAllMonitorsAsync();
            await runtime.CloseAllMonitorTabsAsync(cancellationToken);
            await runtime.DelayAsync(TimeSpan.FromMilliseconds(700), cancellationToken);

            var firstMonitor = monitors[0];
            runtime.LaunchMonitorChrome(string.IsNullOrWhiteSpace(firstMonitor.Url) ? null : firstMonitor.Url);
            await runtime.DelayAsync(TimeSpan.FromMilliseconds(2200), cancellationToken);

            var currentTabs = await runtime.GetTabsAsync(cancellationToken);
            ChromeTab? firstTab = currentTabs.FirstOrDefault(t =>
                string.Equals(t.Url, firstMonitor.Url, StringComparison.OrdinalIgnoreCase))
                ?? currentTabs.FirstOrDefault();

            var outcomes = new List<CrashRecoveryOutcome>(monitors.Count);
            for (var index = 0; index < monitors.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var saved = monitors[index];
                var successKey = $"CrashRecovery.{recoveryId}.Monitor.{saved.Id}.Success";
                var alreadyRecovered = string.Equals(
                    await database.GetSettingAsync(successKey, cancellationToken),
                    "1",
                    StringComparison.Ordinal);

                ChromeTab tab;
                if (index == 0 && firstTab is not null)
                    tab = firstTab;
                else
                {
                    var url = string.IsNullOrWhiteSpace(saved.Url) ? "https://chatgpt.com/" : saved.Url;
                    tab = await runtime.CreateTabAsync(url, cancellationToken);
                }

                // A monitor that already recovered successfully during this incident
                // must never receive the recovery message again on the retry startup.
                // We only restore its monitoring loop on the new tab.
                if (alreadyRecovered)
                {
                    outcomes.Add(CrashRecoveryOutcome.Success);
                    saved.TabId = tab.Id;
                    saved.Title = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;
                    saved.Url = string.IsNullOrWhiteSpace(tab.Url) ? saved.Url : tab.Url;
                    await database.SaveMonitorAsync(saved, cancellationToken);
                    await database.AddLogAsync("System", recoveryMessage, string.Empty, "CrashRecoveryAlreadyVerified", saved.Id, tab.Id, saved.Title, cancellationToken);
                    if (saved.Enabled)
                        await runtime.StartMonitorAsync(saved, tab);
                    continue;
                }

                var sent = await SendWithRetryAsync(runtime, tab, recoveryMessage, cancellationToken);
                var outcome = sent ? CrashRecoveryOutcome.Success : CrashRecoveryOutcome.SendFailed;
                outcomes.Add(outcome);

                saved.TabId = tab.Id;
                saved.Title = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;
                saved.Url = string.IsNullOrWhiteSpace(tab.Url) ? saved.Url : tab.Url;
                await database.SaveMonitorAsync(saved, cancellationToken);

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
                await database.AddLogAsync("System", "CrashRecovery", string.Empty, "CrashRecoveryPartialFailure", cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionLogService.Log(ex, "CrashRecoveryService.RecoverIfPendingAsync");
            await database.AddLogAsync("System", "CrashRecovery", ex.ToString(), "CrashRecoveryFailed", cancellationToken: cancellationToken);
            // Leave CrashRecoveryPending=1 so the next startup can retry.
        }
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