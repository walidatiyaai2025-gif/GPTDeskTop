namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatTransportAnchorRegressionTests
{
    [Fact]
    public void FreshChatPreFirstTurnUsesExactTargetAnchorInsteadOfDurableConversationRequirement()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var method = Slice(
            source,
            "public async Task<bool> EnsureStableConversationTransportAsync",
            "private async Task TryRefreshTabBindingAsync");

        Assert.Contains("hasStableConversationIdentity", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(originalUrl)", method, StringComparison.Ordinal);
        Assert.Contains("var originalTargetId = tab.Id", method, StringComparison.Ordinal);
        Assert.Contains("fresh-chat-target-anchor", method, StringComparison.Ordinal);
        Assert.Contains("string.Equals(candidate.Id, originalTargetId, StringComparison.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(candidate.Url)", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(href)", method, StringComparison.Ordinal);
        Assert.Contains("same-conversation-read-rebind", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalSendStillRequiresStableTransportBeforeComposerAndBeforeNativeInput()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var method = Slice(
            source,
            "public async Task<bool> SendChatMessageAsync",
            "public async Task<bool> SendChatMessageVerifiedAsync");

        Assert.Equal(2, Count(method, "EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3)"));
        Assert.Contains("cdp-transport-not-stable-before-composer", method, StringComparison.Ordinal);
        Assert.Contains("cdp-transport-not-stable-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("TryDispatchNativeSendClickAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryResponseContinuationStillCreatesFreshTargetAndClosesOldOnlyAfterVerifiedHandoff()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var method = Slice(
            source,
            "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync",
            "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");

        Assert.Contains("CreateNewChatTabAsync", method, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab", method, StringComparison.Ordinal);
        Assert.Contains("if (!sent)", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(newTab", method, StringComparison.Ordinal);
        Assert.Contains("CommitVerifiedConversationHandoffAsync", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(oldTab", method, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
