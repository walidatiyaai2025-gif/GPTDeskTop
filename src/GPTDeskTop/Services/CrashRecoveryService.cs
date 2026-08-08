using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class CrashRecoveryService
{
    public static async Task RecoverIfPendingAsync(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
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

        var recoveryMessage = await database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل";
        if (string.IsNullOrWhiteSpace(recoveryMessage)) recoveryMessage = "كمل";

        try
        {
            await monitorService.StopAllAsync();
            await chrome.CloseAllMonitorTabsAsync(cancellationToken);
            await Task.Delay(700, cancellationToken);

            var firstMonitor = monitors[0];
            chrome.LaunchMonitorChrome(string.IsNullOrWhiteSpace(firstMonitor.Url) ? null : firstMonitor.Url);
            await Task.Delay(2200, cancellationToken);

            var currentTabs = await chrome.GetTabsAsync(cancellationToken);
            ChromeTab? firstTab = currentTabs.FirstOrDefault(t =>
                string.Equals(t.Url, firstMonitor.Url, StringComparison.OrdinalIgnoreCase))
                ?? currentTabs.FirstOrDefault();

            var outcomes = new List<CrashRecoveryOutcome>(monitors.Count);
            for (var index = 0; index < monitors.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var saved = monitors[index];
                ChromeTab tab;

                if (index == 0 && firstTab is not null)
                {
                    tab = firstTab;
                }
                else
                {
                    var url = string.IsNullOrWhiteSpace(saved.Url) ? "https://chatgpt.com/" : saved.Url;
                    tab = await chrome.CreateTabAsync(url, cancellationToken);
                }

                var sent = await SendWithRetryAsync(chrome, tab, recoveryMessage, cancellationToken);
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

                if (CrashRecoveryOutcomePolicy.ShouldStartMonitor(outcome, saved.Enabled))
                    await monitorService.StartMonitorAsync(saved, tab);
            }

            if (CrashRecoveryOutcomePolicy.ShouldClearPending(outcomes))
            {
                await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
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
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(attempt == 1 ? 1200 : 700, cancellationToken);
            try
            {
                if (await chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken))
                    return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase))
            {
                // ChatGPT is still rebuilding its DOM; retry after a short delay.
            }
        }
        return false;
    }
}
