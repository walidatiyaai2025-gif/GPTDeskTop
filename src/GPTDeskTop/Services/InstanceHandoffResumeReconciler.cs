using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.Services;

public sealed record InstanceHandoffResumeOutcome(
    long MonitorId,
    string Status,
    string Reason);

public sealed record InstanceHandoffResumeReconciliation(
    IReadOnlyList<InstanceHandoffResumeOutcome> Outcomes)
{
    public int RequestedCount => Outcomes.Count;
    public int ResumedCount => Outcomes.Count(outcome =>
        string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal));
    public int IncompleteCount => RequestedCount - ResumedCount;
    public long[] IncompleteMonitorIds => Outcomes
        .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
        .Select(outcome => outcome.MonitorId)
        .OrderBy(id => id)
        .ToArray();
}

public static class InstanceHandoffResumeReconciler
{
    public static async Task<InstanceHandoffResumeReconciliation> ReconcileAsync(
        InstanceHandoffOffer offer,
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(database);

        var requestedIds = offer.RunningMonitorIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (requestedIds.Length == 0)
            return new InstanceHandoffResumeReconciliation(Array.Empty<InstanceHandoffResumeOutcome>());

        var savedById = (await database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(saved => saved.Id);

        List<ChromeTab>? tabs = null;
        try
        {
            tabs = await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExceptionLogService.Log(ex, "InstanceHandoff.ReconcileChromeTabs");
        }

        var outcomes = new List<InstanceHandoffResumeOutcome>(requestedIds.Length);
        foreach (var monitorId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (monitorService.IsMonitorRunning(monitorId))
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Resumed", "Monitor worker is running after takeover."));
                continue;
            }

            if (!savedById.TryGetValue(monitorId, out var savedMonitor))
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "MissingSavedMonitor"));
                continue;
            }

            if (!savedMonitor.Enabled)
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "Disabled"));
                continue;
            }

            if (!RuntimeHealthPresentation.IsChatGptConversationUrl(savedMonitor.Url))
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "InvalidConversationIdentity"));
                continue;
            }

            if (tabs is null)
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "ChromeUnavailable"));
                continue;
            }

            var resolution = SavedMonitorTabResolver.Resolve(savedMonitor, tabs);
            if (resolution.Tab is null)
            {
                outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "LiveTabUnresolved"));
                continue;
            }

            outcomes.Add(new InstanceHandoffResumeOutcome(monitorId, "Incomplete", "NotRunningAfterResume"));
        }

        return new InstanceHandoffResumeReconciliation(outcomes);
    }
}
