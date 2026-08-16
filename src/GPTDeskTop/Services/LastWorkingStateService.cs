using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record LastWorkingStateResumeOutcome(long MonitorId, string Status, string Reason);

public sealed record LastWorkingStateResumeResult(IReadOnlyList<LastWorkingStateResumeOutcome> Outcomes)
{
    public int RequestedCount => Outcomes.Count;
    public int ResumedCount => Outcomes.Count(outcome => string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal));
    public int IncompleteCount => RequestedCount - ResumedCount;
    public long[] IncompleteMonitorIds => Outcomes
        .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
        .Select(outcome => outcome.MonitorId)
        .Distinct()
        .OrderBy(id => id)
        .ToArray();
}

public static class LastWorkingStateService
{
    public const string DesiredMonitorIdsSetting = "Runtime.DesiredMonitorIds";
    private static readonly SemaphoreSlim PersistenceGate = new(1, 1);

    public static async Task<long[]> GetDesiredMonitorIdsAsync(
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        var raw = await database.GetSettingAsync(DesiredMonitorIdsSetting, cancellationToken).ConfigureAwait(false);
        return ParseMonitorIds(raw);
    }

    public static async Task SetMonitorDesiredRunningAsync(
        LocalDatabase database,
        long monitorId,
        bool desiredRunning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (monitorId <= 0) throw new ArgumentOutOfRangeException(nameof(monitorId));

        await PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = ParseMonitorIds(await database.GetSettingAsync(DesiredMonitorIdsSetting, cancellationToken).ConfigureAwait(false))
                .ToHashSet();
            if (desiredRunning) current.Add(monitorId);
            else current.Remove(monitorId);
            await database.SetSettingAsync(
                DesiredMonitorIdsSetting,
                SerializeMonitorIds(current),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PersistenceGate.Release();
        }
    }

    public static async Task ClearDesiredMonitorsAsync(
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await database.SetSettingAsync(DesiredMonitorIdsSetting, string.Empty, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PersistenceGate.Release();
        }
    }

    public static async Task ReplaceDesiredMonitorIdsAsync(
        LocalDatabase database,
        IEnumerable<long> monitorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitorIds);
        await PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await database.SetSettingAsync(
                DesiredMonitorIdsSetting,
                SerializeMonitorIds(monitorIds),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PersistenceGate.Release();
        }
    }

    public static async Task<LastWorkingStateResumeResult> ResumeDesiredMonitorsAsync(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(database);

        var requestedIds = await GetDesiredMonitorIdsAsync(database, cancellationToken).ConfigureAwait(false);
        if (requestedIds.Length == 0)
            return new LastWorkingStateResumeResult(Array.Empty<LastWorkingStateResumeOutcome>());

        var savedById = (await database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(saved => saved.Id);
        var resumable = new List<SavedMonitor>(requestedIds.Length);
        var outcomes = new List<LastWorkingStateResumeOutcome>(requestedIds.Length);

        foreach (var monitorId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!savedById.TryGetValue(monitorId, out var savedMonitor))
            {
                outcomes.Add(new LastWorkingStateResumeOutcome(monitorId, "Incomplete", "MissingSavedMonitor"));
                continue;
            }
            if (!savedMonitor.Enabled)
            {
                outcomes.Add(new LastWorkingStateResumeOutcome(monitorId, "Incomplete", "Disabled"));
                continue;
            }
            if (!RuntimeHealthPresentation.IsChatGptConversationUrl(savedMonitor.Url))
            {
                outcomes.Add(new LastWorkingStateResumeOutcome(monitorId, "Incomplete", "InvalidConversationIdentity"));
                continue;
            }
            resumable.Add(savedMonitor);
        }

        // Invalid/deleted/disabled monitors are intentionally pruned. Valid desired monitors remain
        // persisted even when Chrome is currently closed; each one below can recreate its exact
        // conversation tab and continue after an application restart.
        await ReplaceDesiredMonitorIdsAsync(
            database,
            resumable.Select(saved => saved.Id),
            cancellationToken).ConfigureAwait(false);

        foreach (var savedMonitor in resumable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (monitorService.IsMonitorRunning(savedMonitor.Id))
                {
                    outcomes.Add(new LastWorkingStateResumeOutcome(savedMonitor.Id, "Resumed", "AlreadyRunning"));
                    continue;
                }

                var pendingHandoffTab = await ConversationHandoffCheckpointStore.TryCompleteAcceptedAsync(
                    chrome,
                    database,
                    savedMonitor,
                    cancellationToken).ConfigureAwait(false);
                var pendingHandoffCompleted = pendingHandoffTab is not null;
                var recovery = pendingHandoffCompleted
                    ? new MonitorTabRecoveryResult(pendingHandoffTab!, Recreated: false, BrowserRestarted: false, FollowUpSent: false)
                    : await MonitorTabRecoveryService.EnsureMonitorTabAsync(
                        chrome,
                        database,
                        savedMonitor,
                        sendFollowUpWhenRecreated: true,
                        cancellationToken).ConfigureAwait(false);

                // A recovered tab already receives one follow-up inside MonitorTabRecoveryService.
                // The historical gap was the exact opposite path: when the saved conversation was
                // already open, recovery returned it immediately and startup never issued the first
                // continuation. Send exactly one verified NEW turn before starting the worker so a
                // repeated tail such as "كمل" cannot be mistaken for a fresh receipt.
                var startupFollowUpAttempted = !pendingHandoffCompleted && (recovery.Recreated || !string.IsNullOrWhiteSpace(savedMonitor.AutoReply));
                var startupFollowUpSent = recovery.FollowUpSent;
                if (!pendingHandoffCompleted && !recovery.Recreated && !string.IsNullOrWhiteSpace(savedMonitor.AutoReply))
                {
                    startupFollowUpSent = await SendExistingTabStartupFollowUpAsync(
                        chrome,
                        database,
                        savedMonitor,
                        recovery.Tab,
                        cancellationToken).ConfigureAwait(false);
                }

                // Delivery failure/uncertainty must not prevent the monitor worker from resuming.
                // If ChatGPT was still generating, the normal monitor loop observes the completed
                // response later and continues from that fresh response without a blind resend.
                await monitorService.StartMonitorAsync(savedMonitor, recovery.Tab).ConfigureAwait(false);
                if (monitorService.IsMonitorRunning(savedMonitor.Id))
                {
                    var reason = pendingHandoffCompleted
                        ? "PendingHandoffRecoveredWithoutDuplicateFollowUp"
                        : recovery.Recreated
                        ? startupFollowUpSent
                            ? "RecreatedTabAndFollowUpSent"
                            : "RecreatedTabFollowUpFailed"
                        : !startupFollowUpAttempted
                            ? "PersistedWorkingStateNoFollowUpConfigured"
                            : startupFollowUpSent
                                ? "PersistedWorkingStateAndFollowUpSent"
                                : "PersistedWorkingStateFollowUpDeferred";
                    outcomes.Add(new LastWorkingStateResumeOutcome(savedMonitor.Id, "Resumed", reason));
                }
                else
                {
                    outcomes.Add(new LastWorkingStateResumeOutcome(savedMonitor.Id, "Incomplete", "NotRunningAfterStart"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "LastWorkingState.ResumeMonitor", savedMonitor.Id, savedMonitor.TabId, savedMonitor.Title);
                outcomes.Add(new LastWorkingStateResumeOutcome(savedMonitor.Id, "Incomplete", "StartFailed"));
            }
        }

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
                ExceptionLogService.Log(ex, "LastWorkingState.RestoreChromeVisibility");
            }
        }

        await PersistResumeDiagnosticsAsync(database, outcomes, cancellationToken).ConfigureAwait(false);
        return new LastWorkingStateResumeResult(outcomes.OrderBy(outcome => outcome.MonitorId).ToArray());
    }

    private static async Task<bool> SendExistingTabStartupFollowUpAsync(
        ChromeDevToolsService chrome,
        LocalDatabase database,
        SavedMonitor monitor,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var followUp = monitor.AutoReply.Trim();
        using var flightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url);
        try
        {
            var sent = await chrome.SendChatMessageVerifiedAsync(
                tab,
                followUp,
                cancellationToken,
                requireNewTurn: true).ConfigureAwait(false);

            await database.AddLogAsync(
                "Outbound",
                followUp,
                string.Empty,
                sent ? "StartupResumeFollowUpSent" : "StartupResumeFollowUpDeferred",
                monitor.Id,
                tab.Id,
                tab.Title,
                cancellationToken).ConfigureAwait(false);
            return sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "LastWorkingState.StartupFollowUp", monitor.Id, tab.Id, monitor.Title);
            try
            {
                await database.AddLogAsync(
                    "System",
                    string.Empty,
                    ex.GetType().Name,
                    "StartupResumeFollowUpDeferred",
                    monitor.Id,
                    tab.Id,
                    tab.Title,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception logEx) when (logEx is not OperationCanceledException)
            {
                ExceptionLogService.Log(logEx, "LastWorkingState.StartupFollowUpLog", monitor.Id, tab.Id, monitor.Title);
            }
            return false;
        }
    }

    private static async Task PersistResumeDiagnosticsAsync(
        LocalDatabase database,
        IReadOnlyList<LastWorkingStateResumeOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        var resumed = outcomes.Count(outcome => string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal));
        var incompleteIds = outcomes
            .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
            .Select(outcome => outcome.MonitorId)
            .Distinct()
            .OrderBy(id => id);
        await database.SetSettingsAsync(new Dictionary<string, string>
        {
            ["Runtime.LastResumeUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Runtime.LastResumeRequestedCount"] = outcomes.Count.ToString(),
            ["Runtime.LastResumeResumedCount"] = resumed.ToString(),
            ["Runtime.LastResumeIncompleteIds"] = SerializeMonitorIds(incompleteIds)
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static long[] ParseMonitorIds(string? raw)
        => (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    internal static string SerializeMonitorIds(IEnumerable<long> monitorIds)
        => string.Join(',', monitorIds.Where(id => id > 0).Distinct().OrderBy(id => id));
}
