namespace GPTDeskTop.RuntimeTests;

public sealed class DeferredOutboundRecoveryRegressionTests
{
    private static string MonitorSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void AutoReplyRequiresANewUserTurnReceipt()
    {
        var source = MonitorSource();
        Assert.Contains("requireNewTurn: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UncertainOutboundIsDeferredInsteadOfFinalFailure()
    {
        var source = MonitorSource();
        Assert.Contains("PendingAutoReply", source, StringComparison.Ordinal);
        Assert.Contains("\"Deferred\"", source, StringComparison.Ordinal);
        Assert.Contains("SentAfterRecovery", source, StringComparison.Ordinal);
        Assert.Contains("DeferredOutboundResolvedByConversationAdvance", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryCleansOnlyDuplicateTargetsForTheSameConversation()
    {
        var source = MonitorSource();
        Assert.Contains("CleanupRecoveredConversationTabsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(currentTab.Url, candidate.Url)", source, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(candidate.Id, currentTab.Id", source, StringComparison.Ordinal);
    }
}
