namespace GPTDeskTop.RuntimeTests;

public sealed class PhysicalSubmitAcceptanceRegressionTests
{
    [Fact]
    public void JavascriptClickAloneIsNotTreatedAsPhysicalSubmitAcceptance()
    {
        var source = ChromeSource();
        var send = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");

        Assert.Contains("maxUnacceptedClickAttempts = 3", send, StringComparison.Ordinal);
        Assert.Contains("ObserveImmediatePhysicalSubmitAsync", send, StringComparison.Ordinal);
        Assert.Contains("ImmediatePhysicalSubmitObservation.ClickNotAccepted", send, StringComparison.Ordinal);
        Assert.Contains("click-not-accepted-composer-still-ready", send, StringComparison.Ordinal);
        Assert.Contains("physical-click-not-accepted", send, StringComparison.Ordinal);
        Assert.Contains("physical-submit-ambiguous-after-click", send, StringComparison.Ordinal);
        Assert.DoesNotContain("\"physical-submit-unacknowledged\"", send, StringComparison.Ordinal);
    }

    [Fact]
    public void StillReadyUnchangedComposerMayRetryOnlyWithoutConsumingExactlyOnceBudget()
    {
        var source = ChromeSource();
        var send = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");
        var branchStart = send.IndexOf("if (immediateObservation == ImmediatePhysicalSubmitObservation.ClickNotAccepted)", StringComparison.Ordinal);
        var branchEnd = send.IndexOf("// The click happened but the UI no longer gives enough evidence", branchStart, StringComparison.Ordinal);
        Assert.True(branchStart >= 0 && branchEnd > branchStart);
        var branch = send[branchStart..branchEnd];

        Assert.Contains("unacceptedClickAttempts++", branch, StringComparison.Ordinal);
        Assert.Contains("unacceptedClickAttempts >= maxUnacceptedClickAttempts", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("submitAttempts++", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("unacknowledgedSubmitSinceUtc =", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmediateObserverRequiresRealTransitionEvidenceAndNeverReloads()
    {
        var source = ChromeSource();
        var observer = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> RefreshStuckComposerAsync");

        Assert.Contains("snapshot.Count > baselineUserTurnCount", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.IsGenerating", observer, StringComparison.Ordinal);
        Assert.Contains("composer.Text.Length == 0", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent && readiness.SendButtonEnabled", observer, StringComparison.Ordinal);
        Assert.Contains("stableStillReadyReads >= 3", observer, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", observer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", observer, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalRateLimitModalBlocksComposerSubmission()
    {
        var source = ChromeSource();
        var readiness = Slice(source, "private async Task<ComposerReadinessSnapshot> ReadComposerReadinessAsync", "private async Task<ComposerAutomationDecision> ReadComposerDecisionAsync");
        Assert.Contains("chatState.GlobalRateLimitText", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentityIsV203IncludingInstallerRegistryVersion()
    {
        var root = Root();
        Assert.Contains("<Version>2.0.3</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "GPTDeskTop.csproj")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.3</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj")), StringComparison.Ordinal);
        Assert.Contains("internal const string Version = \"2.0.3\";", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "Program.cs")), StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
