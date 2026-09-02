namespace GPTDeskTop.RuntimeTests;

public sealed class StuckComposerClickRetryRegressionTests
{
    [Fact]
    public void ImmediateObservationDoesNotDependOnSendButtonSelectorForSafeRetry()
    {
        var source = ChromeSource();
        var method = Slice(source,
            "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync",
            "private async Task<bool> TryDispatchNativeSendClickAsync");

        Assert.Contains("exact-composer-still-present-after-click", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorPresent && readiness.EditorEnabled", method, StringComparison.Ordinal);
        Assert.DoesNotContain("if (readiness.SendButtonPresent && readiness.SendButtonEnabled)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationAuthorizesRetryFromStableExactComposerWithoutSendButtonSelector()
    {
        var source = ChromeSource();
        var method = Slice(source,
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("stable-exact-composer-proves-submit-not-accepted", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEvidenceTextEquals(composer.Text, expected)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("&& composerReadiness.SendButtonPresent", method, StringComparison.Ordinal);
    }

    [Fact]
    public void StableExactComposerRequiresSixReadsBeforeRetryAuthorization()
    {
        var source = ChromeSource();
        Assert.Contains("stableStillReadyReads >= 6", source, StringComparison.Ordinal);
        Assert.Contains("stableReadyComposerReads >= 6", source, StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
