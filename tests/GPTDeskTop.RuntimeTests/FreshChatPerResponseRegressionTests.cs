namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatPerResponseRegressionTests
{
    [Fact]
    public void NormalResponseAlwaysMovesContinuationToBrandNewChat()
    {
        var source = ReadMonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync");
        Assert.Contains("if (!isError)", loop, StringComparison.Ordinal);
        Assert.Contains("ContinueInFreshChatAfterResponseAsync(", loop, StringComparison.Ordinal);
        Assert.Contains("continue;", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshChatContinuationUsesExactConfiguredFollowUpAndClosesOldChatAfterVerifiedHandoff()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        Assert.Contains("var startMessage = monitor.AutoReply.Trim();", method, StringComparison.Ordinal);
        Assert.Contains("CreateNewChatTabAsync", method, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab, startMessage", method, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"EveryResponseFreshChat\"", method, StringComparison.Ordinal);
        Assert.Contains("CommitVerifiedConversationHandoffAsync", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(oldTab", method, StringComparison.Ordinal);
        Assert.Contains("Old chat closed", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedFreshChatSendNeverFallsBackToOldConversation()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        Assert.Contains("if (!sent)", method, StringComparison.Ordinal);
        Assert.Contains("old conversation remains untouched", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(newTab", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    private static string ReadMonitorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
