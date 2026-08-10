using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class InstanceHandoffResumeReconciliationTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ReconciliationCountsRequestedResumedAndIncompleteDeterministically()
    {
        var result = new InstanceHandoffResumeReconciliation(new[]
        {
            new InstanceHandoffResumeOutcome(3, "Resumed", "Monitor worker is running after takeover."),
            new InstanceHandoffResumeOutcome(7, "Incomplete", "LiveTabUnresolved"),
            new InstanceHandoffResumeOutcome(11, "Incomplete", "MissingSavedMonitor")
        });

        Assert.Equal(3, result.RequestedCount);
        Assert.Equal(1, result.ResumedCount);
        Assert.Equal(2, result.IncompleteCount);
        Assert.Equal(new long[] { 7, 11 }, result.IncompleteMonitorIds);
    }

    [Fact]
    public void ReconcilerAccountsEveryDistinctPositiveRequestedMonitorId()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffResumeReconciler.cs");

        Assert.Contains(".Where(id => id > 0)", source, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var monitorId in requestedIds)", source, StringComparison.Ordinal);
        Assert.Contains("monitorService.IsMonitorRunning(monitorId)", source, StringComparison.Ordinal);
        Assert.Contains("MissingSavedMonitor", source, StringComparison.Ordinal);
        Assert.Contains("Disabled", source, StringComparison.Ordinal);
        Assert.Contains("InvalidConversationIdentity", source, StringComparison.Ordinal);
        Assert.Contains("ChromeUnavailable", source, StringComparison.Ordinal);
        Assert.Contains("LiveTabUnresolved", source, StringComparison.Ordinal);
        Assert.Contains("NotRunningAfterResume", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramPersistsTakeoverResumeAccountingAndLogsPartialResume()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var resume = source.IndexOf("ResumeRunningMonitorsAsync", StringComparison.Ordinal);
        var reconcile = source.IndexOf("InstanceHandoffResumeReconciler.ReconcileAsync", resume, StringComparison.Ordinal);
        var requested = source.IndexOf("LastInstanceHandoffRequestedCount", reconcile, StringComparison.Ordinal);
        var resumed = source.IndexOf("LastInstanceHandoffResumedCount", requested, StringComparison.Ordinal);
        var incomplete = source.IndexOf("LastInstanceHandoffIncompleteCount", resumed, StringComparison.Ordinal);
        var incompleteIds = source.IndexOf("LastInstanceHandoffIncompleteIds", incomplete, StringComparison.Ordinal);
        var diagnostic = source.IndexOf("Program.InstanceHandoffResumeIncomplete", incompleteIds, StringComparison.Ordinal);

        Assert.True(resume >= 0 && reconcile > resume,
            "Resume reconciliation must run after the existing takeover resume operation.");
        Assert.True(requested > reconcile && resumed > requested && incomplete > resumed && incompleteIds > incomplete,
            "Program must persist requested/resumed/incomplete counts and incomplete IDs in a stable order.");
        Assert.True(diagnostic > incompleteIds,
            "A partial takeover resume must emit one summarized diagnostic instead of remaining silent.");
    }
}
