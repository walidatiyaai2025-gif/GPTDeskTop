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
