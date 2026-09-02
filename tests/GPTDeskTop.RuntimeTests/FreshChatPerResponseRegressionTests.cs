namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatPerResponseRegressionTests
{
    [Fact]
    public void NormalResponseAlwaysMovesContinuationToBrandNewChat()
    {
        var source = ReadMonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<FreshChatContinuationResult> ContinueInFreshChatAfterResponseAsync");
        Assert.Contains("if (!isError)", loop, StringComparison.Ordinal);
        Assert.Contains("ContinueInFreshChatAfterResponseAsync(", loop, StringComparison.Ordinal);
        Assert.Contains("continue;", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshChatContinuationUsesExactConfiguredFollowUpAndClosesOldChatAfterVerifiedHandoff()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<FreshChatContinuationResult> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        Assert.Contains("var startMessage = monitor.AutoReply.Trim();", method, StringComparison.Ordinal);
        Assert.Contains("CreateNewChatTabForFreshHandoffAsync(monitor.Id", method, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab, startMessage", method, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"EveryResponseFreshChat\"", method, StringComparison.Ordinal);
        Assert.Contains("CommitVerifiedConversationHandoffAsync", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(oldTab", method, StringComparison.Ordinal);
        Assert.Contains("Old chat closed", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PreSubmitFreshChatDeferralRetriesSameCompletedResponseWithoutOldChatSend()
    {
        var source = ReadMonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<FreshChatContinuationResult> ContinueInFreshChatAfterResponseAsync");
        var method = Slice(source, "private async Task<FreshChatContinuationResult> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");

        Assert.Contains("else if (freshResult.RetrySourceResponse)", loop, StringComparison.Ordinal);
        Assert.Contains("lastHandledText = string.Empty;", loop, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyOutcome.DeferredBeforePhysicalSubmit", method, StringComparison.Ordinal);
        Assert.Contains("FreshChatFollowUpPreSubmitDeferred", method, StringComparison.Ordinal);
        Assert.Contains("RetrySourceResponse: true", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(newTab", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    [Fact]
    public void UncertainFreshChatDeliveryPreservesExactlyOnceFence()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<FreshChatContinuationResult> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        var uncertain = Slice(method, "if (sendOutcome == SendWhenReadyOutcome.ReconcileRequired)", "var committedTab = await CommitVerifiedConversationHandoffAsync");

        Assert.Contains("Exactly-once safety preserves the target/checkpoint", uncertain, StringComparison.Ordinal);
        Assert.Contains("FreshChatFollowUpReconcileRequired", uncertain, StringComparison.Ordinal);
        Assert.Contains("RetrySourceResponse: false", uncertain, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversationHandoffCheckpointStore.ClearAsync", uncertain, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseTabAsync(newTab", uncertain, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankNewChatUrlCanonicalizationDoesNotResetStableDwell()
    {
        var source = ReadMonitorSource();
        var helper = Slice(source, "private static bool IsStableSendTargetContinuityPreserved", "private async Task<bool> WaitForStableSendWindowAsync");
        var dwell = Slice(source, "private async Task<bool> WaitForStableSendWindowAsync", "private async Task<SendWhenReadyOutcome> SendWhenReadyAsync");

        Assert.Contains("return !currentHasConversationIdentity;", helper, StringComparison.Ordinal);
        Assert.Contains("blank-new-chat-url-canonicalized", dwell, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(candidate => string.Equals(candidate.Id, expectedTabId", dwell, StringComparison.Ordinal);
        Assert.Contains("IsStableSendTargetContinuityPreserved(expectedUrl, liveTab.Url)", dwell, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(liveTab.Url, expectedUrl, StringComparison.Ordinal))\r\n        {\r\n            Activity?.Invoke(monitorId, \"15-second dwell reset", dwell, StringComparison.Ordinal);
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
