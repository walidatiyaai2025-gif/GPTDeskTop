using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Conservative recovery for a broken Chrome/CDP session. This is for transport/browser
/// failures only; it never attempts to bypass ChatGPT usage or rate limits.
/// </summary>
public sealed class ChromeRecoveryService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;

    public ChromeRecoveryService(ChromeDevToolsService chrome, LocalDatabase database)
    {
        _chrome = chrome;
        _database = database;
    }

    public async Task<Dictionary<long, ChromeTab>> RecoverAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var crashCount = await _database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue, cancellationToken);
            await _database.SetSettingAsync("CrashCount", checked((crashCount + 1).ToString()), cancellationToken);

            var monitors = (await _database.GetSavedMonitorsAsync(cancellationToken))
                .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Url))
                .ToList();

            await _database.AddLogAsync("System", "Chrome recovery", reason, "ChromeRecoveryStarted", null, null, null, cancellationToken);

            // Reuse the existing public Chrome lifecycle API. First close the controllable
            // monitor tabs, then launch a fresh monitor Chrome process/profile.
            await _chrome.CloseAllMonitorTabsAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);
            _chrome.LaunchMonitorChrome();
            await WaitForChromeAsync(cancellationToken);

            var result = new Dictionary<long, ChromeTab>();
            foreach (var monitor in monitors)
            {
                try
                {
                    var tab = await _chrome.CreateTabAsync(monitor.Url, cancellationToken);
                    await Task.Delay(800, cancellationToken);

                    var recoveryMessage = string.IsNullOrWhiteSpace(monitor.NewChatStartMessage)
                        ? "كمل"
                        : monitor.NewChatStartMessage;
                    var sent = await _chrome.SendChatMessageAsync(tab, recoveryMessage, cancellationToken);

                    monitor.TabId = tab.Id;
                    monitor.Title = string.IsNullOrWhiteSpace(tab.Title) ? monitor.Title : tab.Title;
                    monitor.Url = tab.Url;
                    await _database.SaveMonitorAsync(monitor, cancellationToken);
                    await _database.AddLogAsync(
                        "System",
                        recoveryMessage,
                        reason,
                        sent ? "ChromeRecoveryReopened" : "ChromeRecoveryReopenedSendFailed",
                        monitor.Id,
                        tab.Id,
                        monitor.Title,
                        cancellationToken);
                    result[monitor.Id] = tab;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ExceptionLogService.LogAsync(ex, "ChromeRecoveryService.ReopenMonitor", monitor.Id, monitor.TabId, monitor.Title);
                }
            }

            await _database.AddLogAsync("System", "Chrome recovery", $"Recovered {result.Count}/{monitors.Count} monitors.", "ChromeRecoveryCompleted", null, null, null, cancellationToken);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task WaitForChromeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await _chrome.GetTabsAsync(cancellationToken);
                if (tabs.Count > 0) return;
            }
            catch when (attempt < 30)
            {
                // Chrome's DevTools endpoint can take a short time to become available.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Chrome DevTools endpoint did not become ready after recovery.");
    }
}
