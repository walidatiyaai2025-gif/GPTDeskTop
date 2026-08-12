namespace GPTDeskTop.RuntimeTests;

public sealed class ChatGptRotationHandoffRegressionTests
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
    public void DeferredContextLimitRotationDoesNotRearmUnchangedTerminalResponse()
    {
        var source = MonitorSource();
        var rotationStart = source.IndexOf(
            "if (monitor.ConversationRotationEnabled && IsConversationContextLimit(text))",
            StringComparison.Ordinal);
        var deferred = source.IndexOf("\"RotationHandoffDeferred\"", rotationStart, StringComparison.Ordinal);
        var deferredContinue = source.IndexOf("continue;", deferred, StringComparison.Ordinal);

        Assert.True(rotationStart >= 0);
        Assert.True(deferred > rotationStart);
        Assert.True(deferredContinue > deferred);

        var deferredBlock = source[deferred..deferredContinue];
        Assert.DoesNotContain("lastHandledText = string.Empty", deferredBlock, StringComparison.Ordinal);
        Assert.Contains("automatic duplicate retry is suppressed", deferredBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "rotation handoff could not be sent after waiting for the composer",
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextLimitCommitFailureDoesNotResendAcceptedHandoff()
    {
        var source = MonitorSource();
        var rotationStart = source.IndexOf(
            "if (monitor.ConversationRotationEnabled && IsConversationContextLimit(text))",
            StringComparison.Ordinal);
        var commitCall = source.IndexOf(
            "var committedTab = await CommitVerifiedConversationHandoffAsync",
            rotationStart,
            StringComparison.Ordinal);
        var commitFailure = source.IndexOf("if (committedTab is null)", commitCall, StringComparison.Ordinal);
        var commitFailureContinue = source.IndexOf("continue;", commitFailure, StringComparison.Ordinal);

        Assert.True(rotationStart >= 0);
        Assert.True(commitCall > rotationStart);
        Assert.True(commitFailure > commitCall);
        Assert.True(commitFailureContinue > commitFailure);

        var commitFailureBlock = source[commitFailure..commitFailureContinue];
        Assert.DoesNotContain("lastHandledText = string.Empty", commitFailureBlock, StringComparison.Ordinal);
        Assert.Contains("Automatic re-send is suppressed", commitFailureBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void NewChatDeliveryCanRecoverWithoutReloadingNormalAutoReplyTab()
    {
        var source = MonitorSource();

        Assert.Contains(
            "SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply, allowRecoveryReload: false",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Composer still unavailable. Reloading only the newly-created chat once before retrying delivery.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverySendFailureIsDeferredForRetryInsteadOfBecomingFatal()
    {
        var source = MonitorSource();
        var recoveryStart = source.IndexOf("if (isError && IsDeliveryTimeout(text))", StringComparison.Ordinal);
        var deferred = source.IndexOf("\"RecoverySendDeferred\"", recoveryStart, StringComparison.Ordinal);
        var resetHandledText = source.IndexOf("lastHandledText = string.Empty", deferred, StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0);
        Assert.True(deferred > recoveryStart);
        Assert.True(resetHandledText > deferred);
    }
}
