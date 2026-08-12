using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record MonitorTabRecoveryResult(
    ChromeTab Tab,
    bool Recreated,
    bool BrowserRestarted,
    bool FollowUpSent);

public static class MonitorTabRecoveryService
{
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);
    private static readonly TimeSpan ChromeRecoveryGracePeriod = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConversationReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FollowUpSendTimeout = TimeSpan.FromSeconds(45);

    public static async Task<MonitorTabRecoveryResult> EnsureMonitorTabAsync(
        ChromeDevToolsService chrome,
        LocalDatabase database,
        SavedMonitor monitor,
        bool sendFollowUpWhenRecreated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitor);

        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            throw new InvalidOperationException("The saved monitor URL is not a stable ChatGPT conversation identity.");

        await RecoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tabs = await TryGetTabsWithGracePeriodAsync(chrome, cancellationToken).ConfigureAwait(false);
            var existing = tabs is null ? null : SavedMonitorTabResolver.Resolve(monitor, tabs).Tab;
            if (existing is not null)
                return new MonitorTabRecoveryResult(existing, Recreated: false, BrowserRestarted: false, FollowUpSent: false);

            var browserRestarted = false;
            ChromeTab recoveredTab;

            if (tabs is { Count: > 0 })
            {
                recoveredTab = await chrome.CreateTabAsync(monitor.Url, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Give any in-flight running-monitor CDP recovery a grace period above. If CDP is
                // still unavailable, tear down only the monitor browser we own and reopen the exact
                // saved conversation. This path is also used after a normal application restart,
                // where the previous monitor Chrome process was intentionally closed.
                try
                {
                    await chrome.CloseAllMonitorTabsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ExceptionLogService.Log(ex, "MonitorTabRecovery.ClosePreviousBrowser", monitor.Id, monitor.TabId, monitor.Title);
                }

                chrome.LaunchMonitorChrome(monitor.Url);
                browserRestarted = true;
                recoveredTab = await WaitForConversationTabAsync(chrome, monitor, cancellationToken).ConfigureAwait(false)
                    ?? throw new TimeoutException($"Monitor #{monitor.Id} conversation did not become available after Chrome recovery.");
            }

            await WaitForChatReadyAsync(chrome, recoveredTab, cancellationToken).ConfigureAwait(false);
            await ApplyConfiguredModelAsync(chrome, monitor, recoveredTab, cancellationToken).ConfigureAwait(false);

            var followUpSent = false;
            if (sendFollowUpWhenRecreated && !string.IsNullOrWhiteSpace(monitor.AutoReply))
            {
                followUpSent = await SendFollowUpOnceAsync(
                    chrome,
                    recoveredTab,
                    monitor.AutoReply,
                    cancellationToken).ConfigureAwait(false);

                await database.AddLogAsync(
                    "Outbound",
                    monitor.AutoReply,
                    string.Empty,
                    followUpSent ? "RestartFollowUpSent" : "RestartFollowUpFailed",
                    monitor.Id,
                    recoveredTab.Id,
                    recoveredTab.Title,
                    cancellationToken).ConfigureAwait(false);
            }

            await database.AddLogAsync(
                "System",
                monitor.Url,
                browserRestarted ? "Monitor Chrome restarted and saved conversation reopened." : "Saved monitor conversation tab recreated.",
                "MonitorTabRecreated",
                monitor.Id,
                recoveredTab.Id,
                recoveredTab.Title,
                cancellationToken).ConfigureAwait(false);

            if (string.Equals(
                    await database.GetSettingAsync("ChromeHidden", cancellationToken).ConfigureAwait(false),
                    "1",
                    StringComparison.Ordinal))
            {
                try
                {
                    await chrome.HideMonitorChromeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ExceptionLogService.Log(ex, "MonitorTabRecovery.RestoreChromeVisibility", monitor.Id, recoveredTab.Id, recoveredTab.Title);
                }
            }

            return new MonitorTabRecoveryResult(recoveredTab, Recreated: true, browserRestarted, followUpSent);
        }
        finally
        {
            RecoveryGate.Release();
        }
    }

    private static async Task<List<ChromeTab>?> TryGetTabsWithGracePeriodAsync(
        ChromeDevToolsService chrome,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ChromeRecoveryGracePeriod;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                    return null;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return null;
    }

    private static async Task<ChromeTab?> WaitForConversationTabAsync(
        ChromeDevToolsService chrome,
        SavedMonitor monitor,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ConversationReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                var resolved = SavedMonitorTabResolver.Resolve(monitor, tabs).Tab;
                if (resolved is not null)
                    return resolved;

                // Chrome may have started with an intermediate page before honoring the exact URL.
                // Once a controllable page exists, create the saved conversation target explicitly.
                if (tabs.Count > 0)
                {
                    try
                    {
                        return await chrome.CreateTabAsync(monitor.Url, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Continue the bounded wait; Chrome may still be initializing its CDP targets.
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Expected while the new Chrome process is binding the CDP endpoint.
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task WaitForChatReadyAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ConversationReadyTimeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await chrome.GetChatStateAsync(tab, cancellationToken).ConfigureAwait(false);
                if (!state.IsGenerating)
                    return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Recovered ChatGPT conversation did not become ready within {ConversationReadyTimeout.TotalSeconds:0} seconds."
            + (lastError is null ? string.Empty : $" Last error: {lastError.Message}"));
    }

    private static async Task ApplyConfiguredModelAsync(
        ChromeDevToolsService chrome,
        SavedMonitor monitor,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        if (!monitor.ModelRoutingEnabled)
            return;

        var preferredSelected = await chrome.TrySelectModelAsync(
            tab,
            monitor.PreferredModel,
            cancellationToken).ConfigureAwait(false);
        if (preferredSelected)
            return;

        if (!string.IsNullOrWhiteSpace(monitor.FallbackModel)
            && !string.Equals(monitor.FallbackModel, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            await chrome.TrySelectModelAsync(tab, monitor.FallbackModel, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> SendFollowUpOnceAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        var followUp = message.Trim();
        if (followUp.Length == 0)
            return false;

        // This intentionally uses the composer click receipt rather than the generic
        // SendChatMessageVerifiedAsync idempotency shortcut. Recovery is required to create a NEW
        // continuation turn even when the previous user turn used the same repeated text (for
        // example "كمل"). Stop immediately after the first successful send-button click.
        var deadline = DateTimeOffset.UtcNow + FollowUpSendTimeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await chrome.SendChatMessageAsync(tab, followUp, cancellationToken).ConfigureAwait(false))
                    return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        }

        if (lastError is not null)
            ExceptionLogService.Log(lastError, "MonitorTabRecovery.SendFollowUp", null, tab.Id, tab.Title);
        return false;
    }
}
