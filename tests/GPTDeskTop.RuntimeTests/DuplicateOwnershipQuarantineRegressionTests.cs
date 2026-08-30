namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipQuarantineRegressionTests
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
    public void DevelopmentDeliveryQuarantinesDuplicateOwnersBeforeOptInOrTabResolution()
    {
        var source = ReadSource(
            "src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskMonitorTargetFactory.cs");

        var analyzer = source.IndexOf("MonitorConversationOwnership.FindDuplicateMonitorIds(monitors)", StringComparison.Ordinal);
        var loop = source.IndexOf("foreach (var monitor in monitors.Where(x => x.Enabled))", analyzer, StringComparison.Ordinal);
        var duplicateGuard = source.IndexOf("if (duplicateMonitorIds.Contains(monitor.Id))", loop, StringComparison.Ordinal);
        var diagnostic = source.IndexOf("DevelopmentMonitorDuplicateConversationOwnership", duplicateGuard, StringComparison.Ordinal);
        var optIn = source.IndexOf("DevelopmentPlanMonitorSettings.IsEnabledAsync", duplicateGuard, StringComparison.Ordinal);
        var resolution = source.IndexOf("SavedMonitorTabResolver.Resolve(monitor, tabs)", duplicateGuard, StringComparison.Ordinal);

        Assert.True(analyzer >= 0);
        Assert.True(loop > analyzer);
        Assert.True(duplicateGuard > loop);
        Assert.True(diagnostic > duplicateGuard);
        Assert.True(optIn > diagnostic);
        Assert.True(resolution > optIn);
        Assert.Contains("continue;", source[duplicateGuard..optIn], StringComparison.Ordinal);
    }

    [Fact]
    public void CrashRecoveryQuarantinesDuplicateOwnersBeforeFirstTargetSelectionAndPerMonitorDelivery()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryService.cs");

        var analyzer = source.IndexOf("MonitorConversationOwnership.FindDuplicateMonitorIds(monitors)", StringComparison.Ordinal);
        var validSelection = source.IndexOf("var validMonitors = monitors", analyzer, StringComparison.Ordinal);
        var exclude = source.IndexOf("!duplicateMonitorIds.Contains(saved.Id)", validSelection, StringComparison.Ordinal);
        var loop = source.IndexOf("foreach (var saved in monitors)", exclude, StringComparison.Ordinal);
        var duplicateGuard = source.IndexOf("if (duplicateMonitorIds.Contains(saved.Id))", loop, StringComparison.Ordinal);
        var outcome = source.IndexOf("CrashRecoveryOutcome.DuplicateConversationOwnership", duplicateGuard, StringComparison.Ordinal);
        var diagnostic = source.IndexOf("CrashRecoveryDuplicateConversationOwnership", outcome, StringComparison.Ordinal);
        var successKey = source.IndexOf("var successKey =", duplicateGuard, StringComparison.Ordinal);

        Assert.True(analyzer >= 0);
        Assert.True(validSelection > analyzer);
        Assert.True(exclude > validSelection);
        Assert.True(loop > exclude);
        Assert.True(duplicateGuard > loop);
        Assert.True(outcome > duplicateGuard);
        Assert.True(diagnostic > outcome);
        Assert.True(successKey > diagnostic);
        Assert.Contains("continue;", source[duplicateGuard..successKey], StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomePolicyCannotClearPendingOrStartMonitorForDuplicateOwnership()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryOutcomePolicy.cs");

        Assert.Contains("DuplicateConversationOwnership", source, StringComparison.Ordinal);
        Assert.Contains("outcome == CrashRecoveryOutcome.Success", source, StringComparison.Ordinal);
        Assert.Contains("outcomes.All(x => x == CrashRecoveryOutcome.Success)", source, StringComparison.Ordinal);
    }
}
