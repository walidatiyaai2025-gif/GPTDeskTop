using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class StuckComposerRecoveryRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void GeneratingComposerNeverRequestsRefreshRegardlessOfElapsedTime()
    {
        var snapshot = new ComposerReadinessSnapshot(
            IsGenerating: true,
            EditorPresent: true,
            EditorEnabled: true,
            SendButtonPresent: true,
            SendButtonEnabled: false,
            HasRenderedError: false);

        Assert.False(StuckComposerRecoveryPolicy.ShouldRefresh(
            snapshot,
            editorMatchesExpectedAutomationText: true,
            blockedFor: TimeSpan.FromMinutes(10),
            refreshAlreadyUsed: false));
    }

    [Fact]
    public void PostGenerationDisabledSendMustRemainBlockedForThreshold()
    {
        var snapshot = BlockedSnapshot();

        Assert.False(StuckComposerRecoveryPolicy.ShouldRefresh(
            snapshot, true,
            StuckComposerRecoveryPolicy.DefaultThreshold - TimeSpan.FromMilliseconds(1),
            refreshAlreadyUsed: false));
        Assert.True(StuckComposerRecoveryPolicy.ShouldRefresh(
            snapshot, true,
            StuckComposerRecoveryPolicy.DefaultThreshold,
            refreshAlreadyUsed: false));
    }

    [Fact]
    public void ManualEditorChangeOrPriorRefreshSuppressesRecoveryReload()
    {
        var snapshot = BlockedSnapshot();
        var elapsed = StuckComposerRecoveryPolicy.DefaultThreshold + TimeSpan.FromSeconds(30);

        Assert.False(StuckComposerRecoveryPolicy.ShouldRefresh(snapshot, false, elapsed, refreshAlreadyUsed: false));
        Assert.False(StuckComposerRecoveryPolicy.ShouldRefresh(snapshot, true, elapsed, refreshAlreadyUsed: true));
    }

    [Fact]
    public void RenderedErrorDoesNotMasqueradeAsStuckSend()
    {
        var snapshot = BlockedSnapshot() with { HasRenderedError = true };

        Assert.False(snapshot.IsPostGenerationSendBlocked);
        Assert.False(StuckComposerRecoveryPolicy.ShouldRefresh(
            snapshot, true,
            TimeSpan.FromMinutes(1),
            refreshAlreadyUsed: false));
    }

    [Fact]
    public void VerifiedSendSourceUsesOneSameConversationRefreshAndReceiptChecks()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("StuckComposerRecoveryPolicy.ShouldRefresh", source, StringComparison.Ordinal);
        Assert.Contains("RefreshStuckComposerAsync", source, StringComparison.Ordinal);
        Assert.Contains("ComposerEditorMatchesExpectedAsync", source, StringComparison.Ordinal);
        Assert.Contains("stuckRefreshUsed = true", source, StringComparison.Ordinal);
        Assert.Contains("receiptBeforeRefresh", source, StringComparison.Ordinal);
        Assert.Contains("receiptAfterRefresh", source, StringComparison.Ordinal);
        Assert.Contains("TryFindConversationTabAsync(tab.Url", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(tab.Url, replacement.Url)", source, StringComparison.Ordinal);
    }

    private static ComposerReadinessSnapshot BlockedSnapshot()
        => new(
            IsGenerating: false,
            EditorPresent: true,
            EditorEnabled: true,
            SendButtonPresent: true,
            SendButtonEnabled: false,
            HasRenderedError: false);
}
