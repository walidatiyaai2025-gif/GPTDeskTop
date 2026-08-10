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
            new InstanceHandoffResumeOutcome(11, "Incomplete", "StartFailed")
        });

        Assert.Equal(3, result.RequestedCount);
        Assert.Equal(1, result.ResumedCount);
        Assert.Equal(2, result.IncompleteCount);
        Assert.Equal(new long[] { 7, 11 }, result.IncompleteMonitorIds);
    }

    [Fact]
    public void ResumeLoopAccountsEveryRequestedMonitorWithBoundedReasons()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var start = source.IndexOf("public static async Task<InstanceHandoffResumeReconciliation> ResumeRunningMonitorsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RunServerAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains(".Where(id => id > 0)", method, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", method, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(id => id)", method, StringComparison.Ordinal);
        Assert.Contains("foreach (var monitorId in requestedIds)", method, StringComparison.Ordinal);
        Assert.Contains("MissingSavedMonitor", method, StringComparison.Ordinal);
        Assert.Contains("Disabled", method, StringComparison.Ordinal);
        Assert.Contains("InvalidConversationIdentity", method, StringComparison.Ordinal);
        Assert.Contains("ChromeUnavailable", method, StringComparison.Ordinal);
        Assert.Contains("LiveTabUnresolved", method, StringComparison.Ordinal);
        Assert.Contains("StartFailed", method, StringComparison.Ordinal);
        Assert.Contains("NotRunningAfterStart", method, StringComparison.Ordinal);
        Assert.Contains("for (var attempt = 1; attempt <= 20; attempt++)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Monitor Chrome did not become reachable during instance handoff resume", method, StringComparison.Ordinal);
    }

    [Fact]
    public void StartFailureAndCompletedButStoppedAreDifferentOutcomes()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var start = source.IndexOf("public static async Task<InstanceHandoffResumeReconciliation> ResumeRunningMonitorsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RunServerAsync", start, StringComparison.Ordinal);
        var method = source[start..end];

        var startCall = method.IndexOf("await monitorService.StartMonitorAsync(savedMonitor, tab)", StringComparison.Ordinal);
        var runningCheck = method.IndexOf("monitorService.IsMonitorRunning(monitorId)", startCall, StringComparison.Ordinal);
        var notRunning = method.IndexOf("NotRunningAfterStart", runningCheck, StringComparison.Ordinal);
        var catchBlock = method.IndexOf("catch (Exception ex) when (ex is not OperationCanceledException)", startCall, StringComparison.Ordinal);
        var startFailed = method.IndexOf("StartFailed", catchBlock, StringComparison.Ordinal);

        Assert.True(startCall >= 0 && runningCheck > startCall && notRunning > runningCheck);
        Assert.True(catchBlock > startCall && startFailed > catchBlock,
            "A thrown monitor start must be represented as StartFailed rather than inferred later as a generic stopped worker.");
    }

    [Fact]
    public void ProgramPersistsTheAuthoritativeResumeResultWithoutSecondPass()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var resume = source.IndexOf("var reconciliation = await InstanceHandoffCoordinator.ResumeRunningMonitorsAsync", StringComparison.Ordinal);
        var requested = source.IndexOf("LastInstanceHandoffRequestedCount", resume, StringComparison.Ordinal);
        var resumed = source.IndexOf("LastInstanceHandoffResumedCount", requested, StringComparison.Ordinal);
        var incomplete = source.IndexOf("LastInstanceHandoffIncompleteCount", resumed, StringComparison.Ordinal);
        var incompleteIds = source.IndexOf("LastInstanceHandoffIncompleteIds", incomplete, StringComparison.Ordinal);
        var diagnostic = source.IndexOf("Program.InstanceHandoffResumeIncomplete", incompleteIds, StringComparison.Ordinal);

        Assert.True(resume >= 0);
        Assert.True(requested > resume && resumed > requested && incomplete > resumed && incompleteIds > incomplete);
        Assert.True(diagnostic > incompleteIds);
        Assert.DoesNotContain("InstanceHandoffResumeReconciler.ReconcileAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationModelHasNoChromeOrDatabaseSecondPass()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffResumeReconciler.cs");

        Assert.Contains("InstanceHandoffResumeOutcome", source, StringComparison.Ordinal);
        Assert.Contains("InstanceHandoffResumeReconciliation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReconcileAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SavedMonitorTabResolver", source, StringComparison.Ordinal);
    }
}
