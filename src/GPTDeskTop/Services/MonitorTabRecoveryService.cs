using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.Services;

public sealed record MonitorTabRecoveryResult(
    ChromeTab Tab,
    bool Recreated,
    bool BrowserRestarted,
    bool FollowUpSent);

public static class MonitorTabRecoveryService
{
    private static readonly SemaphoreSlim RecoveryGate = new(1, 1);
    private static readonly TimeSpan ChromeRecoveryGracePeriod = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ConversationReadyTimeout = TimeSpan.FromSeconds(60);

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
            {
                await PersistRuntimeTargetAsync(database, monitor, existing, cancellationToken).ConfigureAwait(false);
                return new MonitorTabRecoveryResult(existing, Recreated: false, BrowserRestarted: false, FollowUpSent: false);
            }

            var browserRestarted = false;
            ChromeTab recoveredTab;

            if (tabs is not null)
            {
                // /json/list succeeded, so CDP is healthy even when it currently reports zero pages.
                // An empty target list is not a browser crash. Recreate only the saved conversation
                // and keep the monitor browser/process alive; this is the video-repro reacquisition path.
                recoveredTab = await chrome.CreateTabAsync(monitor.Url, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Only a genuinely unavailable CDP endpoint is allowed to restart the monitor browser.
                // Never tear Chrome down merely because a healthy endpoint temporarily has no targets.
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

            var recoveredState = await WaitForChatReachableAsync(chrome, recoveredTab, cancellationToken).ConfigureAwait(false);
            await PersistRuntimeTargetAsync(database, monitor, recoveredTab, cancellationToken).ConfigureAwait(false);

            // Model switching and continuation sends are mutations. Never perform either while the
            // recovered conversation is actively generating: reacquisition must be passive and must
            // not interrupt a long ChatGPT response or create a duplicate 'continue' turn.
            if (!recoveredState.IsGenerating)
                await ApplyConfiguredModelAsync(chrome, monitor, recoveredTab, cancellationToken).ConfigureAwait(false);

            var followUpSent = false;
            if (sendFollowUpWhenRecreated
                && !recoveredState.IsGenerating
                && string.IsNullOrWhiteSpace(recoveredState.ErrorText)
                && !string.IsNullOrWhiteSpace(monitor.AutoReply))
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
            else if (recoveredState.IsGenerating)
            {
                await database.AddLogAsync(
                    "System",
                    string.Empty,
                    "Recovered conversation is still generating. Runtime target was rebound without sending a follow-up.",
                    "MonitorTabReboundGenerating",
                    monitor.Id,
                    recoveredTab.Id,
                    recoveredTab.Title,
                    cancellationToken).ConfigureAwait(false);
            }

            await database.AddLogAsync(
                "System",
                monitor.Url,
                browserRestarted
                    ? "Monitor Chrome restarted and saved conversation reopened."
                    : "Saved monitor conversation target reacquired without restarting Chrome.",
                browserRestarted ? "MonitorTabRecreated" : "MonitorTabRebound",
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

    private static async Task PersistRuntimeTargetAsync(
        LocalDatabase database,
        SavedMonitor monitor,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var updated = await database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
            monitor.Id,
            monitor.Url,
            tab.Id,
            tab.Title,
            cancellationToken).ConfigureAwait(false);

        if (!updated)
            throw new InvalidOperationException($"Monitor #{monitor.Id} conversation identity changed while its Chrome target was being rebound.");

        monitor.TabId = tab.Id;
        monitor.Title = tab.Title;
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
                // A successful empty list is a healthy CDP response. Return it immediately so the
                // caller can create the saved conversation target without restarting Chrome.
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
                // Once the CDP endpoint responds, create the saved conversation target explicitly.
                try
                {
                    return await chrome.CreateTabAsync(monitor.Url, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Continue the bounded wait; Chrome may still be initializing its CDP targets.
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

    private static async Task<ChatPageState> WaitForChatReachableAsync(
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
                // A successful state read is enough to establish a healthy target. IsGenerating is
                // intentionally NOT a readiness failure; long responses must remain passive waits.
                return await chrome.GetChatStateAsync(tab, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Recovered ChatGPT conversation did not become reachable within {ConversationReadyTimeout.TotalSeconds:0} seconds."
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

    private static Task<bool> SendFollowUpOnceAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        var followUp = message.Trim();
        if (followUp.Length == 0)
            return Task.FromResult(false);

        return chrome.SendChatMessageVerifiedAsync(
            tab,
            followUp,
            cancellationToken,
            requireNewTurn: true);
    }
}
