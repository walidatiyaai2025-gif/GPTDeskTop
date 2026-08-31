namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendReconciliationLivenessRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void UnacknowledgedSubmitReconciliationHasHardLivenessBudget()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("maxUnacknowledgedReconciliation = TimeSpan.FromSeconds(90)", source, StringComparison.Ordinal);
        Assert.Contains("post-submit-reconciliation-time-budget-exhausted", source, StringComparison.Ordinal);
        Assert.Contains("reconciliationCts.CancelAfter(reconciliationRemaining)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcileRequiredDuplicateGuardDoesNotExpireAfterTwoMinutes()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        Assert.Contains("previous.Phase == OutboundDeliveryPhase.ReconcileRequired", source, StringComparison.Ordinal);
        Assert.Contains("previous.Phase == OutboundDeliveryPhase.Sending", source, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow", source, StringComparison.Ordinal);
    }
}
