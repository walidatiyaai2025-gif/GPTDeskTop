using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services.DevelopmentTaskEngine;

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

        // Invalid/deleted/disabled monitors are intentionally pruned. Valid desired monitors
        // remain persisted even if Chrome is temporarily unavailable so a later restart can retry.
        await ReplaceDesiredMonitorIdsAsync(database, resumable.Select(saved => saved.Id), cancellationToken).ConfigureAwait(false);
        if (resumable.Count == 0)
            return new LastWorkingStateResumeResult(outcomes.OrderBy(outcome => outcome.MonitorId).ToArray());

        List<ChromeTab>? tabs = await TryGetTabsAsync(chrome, cancellationToken).ConfigureAwait(false);
        if (tabs is null)
        {
            try
            {
                chrome.LaunchMonitorChrome(resumable[0].Url);
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "LastWorkingState.LaunchMonitorChrome");
            }
            tabs = await WaitForTabsAsync(chrome, cancellationToken).ConfigureAwait(false);
        }

        if (tabs is null)
        {
            outcomes.AddRange(resumable.Select(saved =>
                new LastWorkingStateResumeOutcome(saved.Id, "Incomplete", "ChromeUnavailable")));
            await PersistResumeDiagnosticsAsync(database, outcomes, cancellationToken).ConfigureAwait(false);
            return new LastWorkingStateResumeResult(outcomes.OrderBy(outcome => outcome.MonitorId).ToArray());
        }

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

                var resolution = SavedMonitorTabResolver.Resolve(savedMonitor, tabs);
                var tab = resolution.Tab;
                if (tab is null)
                {
                    tab = await chrome.CreateTabAsync(savedMonitor.Url, cancellationToken).ConfigureAwait(false);
                    tabs = await WaitForTabsAsync(chrome, cancellationToken).ConfigureAwait(false) ?? tabs;
                    tab = SavedMonitorTabResolver.Resolve(savedMonitor, tabs).Tab ?? tab;
                }

                await monitorService.StartMonitorAsync(savedMonitor, tab).ConfigureAwait(false);
                outcomes.Add(monitorService.IsMonitorRunning(savedMonitor.Id)
                    ? new LastWorkingStateResumeOutcome(savedMonitor.Id, "Resumed", "PersistedWorkingState")
                    : new LastWorkingStateResumeOutcome(savedMonitor.Id, "Incomplete", "NotRunningAfterStart"));

                tabs = await TryGetTabsAsync(chrome, cancellationToken).ConfigureAwait(false) ?? tabs;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "LastWorkingState.ResumeMonitor", savedMonitor.Id, savedMonitor.TabId, savedMonitor.Title);
                outcomes.Add(new LastWorkingStateResumeOutcome(savedMonitor.Id, "Incomplete", "StartFailed"));
            }
        }

        if (string.Equals(await database.GetSettingAsync("ChromeHidden", cancellationToken).ConfigureAwait(false), "1", StringComparison.Ordinal))
        {
            try { await chrome.HideMonitorChromeAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "LastWorkingState.RestoreChromeVisibility");
            }
        }

        await PersistResumeDiagnosticsAsync(database, outcomes, cancellationToken).ConfigureAwait(false);
        return new LastWorkingStateResumeResult(outcomes.OrderBy(outcome => outcome.MonitorId).ToArray());
    }

    private static async Task<List<ChromeTab>?> TryGetTabsAsync(
        ChromeDevToolsService chrome,
        CancellationToken cancellationToken)
    {
        try { return await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionLogService.Log(ex, "LastWorkingState.ReadChromeTabs");
            return null;
        }
    }

    private static async Task<List<ChromeTab>?> WaitForTabsAsync(
        ChromeDevToolsService chrome,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                if (tabs.Count > 0) return tabs;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            if (attempt < 30)
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (lastError is not null)
            ExceptionLogService.Log(lastError, "LastWorkingState.WaitForChrome");
        return null;
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
