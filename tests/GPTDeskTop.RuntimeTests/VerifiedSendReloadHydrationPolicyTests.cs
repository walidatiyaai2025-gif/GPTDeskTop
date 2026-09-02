using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendReloadHydrationPolicyTests
{
    [Theory]
    [InlineData(false, 7, 0, "", "كمل", "Hydrating")]
    [InlineData(true, 7, 4, "كمل", "كمل", "Hydrating")]
    [InlineData(true, 7, 7, "كمل", "كمل", "StableBaseline")]
    [InlineData(true, 7, 8, "كمل", "كمل", "ReceiptConfirmed")]
    [InlineData(true, 7, 8, "", "كمل", "Hydrating")]
    [InlineData(true, 7, 8, "manual message", "كمل", "UnexpectedChange")]
    public void PostRefreshUserTurnClassificationIsHydrationAware(
        bool readable,
        int baselineCount,
        int observedCount,
        string observedLastText,
        string expectedText,
        string expectedDecision)
    {
        var policyType = typeof(ChatGptMonitorService).Assembly.GetType(
            "GPTDeskTop.Services.MonitorDeliveryRecoveryPolicy",
            throwOnError: true)!;
        var method = policyType.GetMethod(
            "ClassifyPostRefreshUserTurn",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(policyType.FullName, "ClassifyPostRefreshUserTurn");

        var result = method.Invoke(null, new object[]
        {
            readable,
            baselineCount,
            observedCount,
            observedLastText,
            expectedText
        });

        Assert.Equal(expectedDecision, result!.ToString());
    }

    [Fact]
    public void ReconciliationUsesHydrationPolicyAndNeedsStableUnexpectedEvidence()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        var source = File.ReadAllText(path);
        var start = source.IndexOf(
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("TryRefreshTabBindingAsync(tab, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("PostSubmitReloadSuppressed", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshStuckComposerAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", method, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.ClassifyPostRefreshUserTurn", method, StringComparison.Ordinal);
        Assert.Contains("PostRefreshUserTurnObservation.Hydrating", method, StringComparison.Ordinal);
        Assert.Contains("PostRefreshUserTurnObservation.ReceiptConfirmed", method, StringComparison.Ordinal);
        Assert.Contains("PostRefreshUserTurnObservation.UnexpectedChange", method, StringComparison.Ordinal);
        Assert.Contains("stableUnexpectedReads >= 2", method, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads++;", method, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads >= 4", method, StringComparison.Ordinal);
        Assert.DoesNotContain("if (receiptAfterRefresh.Count != baselineUserTurnCount)\n                return UnacknowledgedSubmitReconciliationResult.Ambiguous;", method, StringComparison.Ordinal);
    }
}
