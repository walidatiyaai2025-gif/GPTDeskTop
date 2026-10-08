using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorDeliveryRecoveryRegressionTests
{
    private static string RepoFile(params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void RepeatedContinuationTextRequiresTurnStateCheck()
    {
        var policy = RepoFile("src", "GPTDeskTop", "Services", "MonitorDeliveryRecoveryPolicy.cs");
        Assert.Contains("assistantMessageCount < userMessageCount", policy, StringComparison.Ordinal);
        Assert.Contains("isGenerating", policy, StringComparison.Ordinal);
        Assert.Contains("if (requireNewTurn) return false", policy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 1, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(4, 5, true)]
    [InlineData(5, 4, false)]
    public void CompletedAssistantTurnDeltaIsPositiveAcceptanceEvidence(
        int baselineAssistantCount,
        int observedAssistantCount,
        bool expected)
        => Assert.Equal(
            expected,
            MonitorDeliveryRecoveryPolicy.HasNewAssistantTurnAfterSubmit(
                baselineAssistantCount,
                observedAssistantCount));

    [Theory]
    [InlineData(-1, 2, "", "", "test", false)]
    [InlineData(0, 0, "", "", "test", false)]
    [InlineData(0, 1, "", "test", "test", true)]
    [InlineData(2, 3, "test", "assistant", "test", true)]
    [InlineData(2, 3, "other", "assistant", "test", false)]
    [InlineData(2, 4, "other", "assistant", "test", true)]
    public void ConversationTurnDeltaProvidesRoleAgnosticReceipt(
        int baselineCount,
        int observedCount,
        string previousText,
        string lastText,
        string expectedText,
        bool expected)
        => Assert.Equal(
            expected,
            MonitorDeliveryRecoveryPolicy.ConversationTurnDeltaConfirmsDelivery(
                baselineCount,
                observedCount,
                previousText,
                lastText,
                expectedText));

    [Fact]
    public void TransportRebindCanFallBackToStableConversationIdentity()
    {
        var policy = RepoFile("src", "GPTDeskTop", "Services", "MonitorDeliveryRecoveryPolicy.cs");
        Assert.Contains("ChatGptConversationIdentity.IsSame(trackedTab.Url, candidate.Url)", policy, StringComparison.Ordinal);
        Assert.Contains("string.Equals(candidate.Id, trackedTab.Id", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void ChromeServiceUsesDeliveryRecoveryPolicy()
    {
        var chrome = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt", chrome, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.FindBestBinding", chrome, StringComparison.Ordinal);
    }
}
