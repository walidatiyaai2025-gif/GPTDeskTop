namespace GPTDeskTop.RuntimeTests;

public sealed class ChatMonitorErrorDrivenWaitRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void MonitorLoopDoesNotUseElapsedTimeAsARefreshTrigger()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("Passive long-response wait ON", source, StringComparison.Ordinal);
        Assert.Contains("if (!isError && (state.IsGenerating || string.IsNullOrWhiteSpace(text)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NoResponseRefreshSeconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"NoResponseRefresh\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lastResponseActivity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCurrentStructuredErrorsDriveGenericFreshChatRecovery()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        var structuredErrorIndex = source.IndexOf("var isError = !state.IsGenerating && !string.IsNullOrWhiteSpace(state.ErrorText)", StringComparison.Ordinal);
        var passiveWaitIndex = source.IndexOf("if (!isError && (state.IsGenerating || string.IsNullOrWhiteSpace(text)", StringComparison.Ordinal);
        Assert.True(structuredErrorIndex >= 0);
        Assert.True(passiveWaitIndex > structuredErrorIndex);
        Assert.Contains("isError && IsDeliveryTimeout(text)", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptErrorContinuationMessage", source, StringComparison.Ordinal);
        Assert.Contains("ChatGPT error saved. Opening a fresh chat and continuing under the same Monitor ID", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"ChatGptError\"", source, StringComparison.Ordinal);
        Assert.Contains("successStatus: \"RecoveredFromChatGptError\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Error saved. Refreshing only this tab", source, StringComparison.Ordinal);
        Assert.Contains("IsConversationContextLimit(text)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsErrorResponse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStateDetectionUsesCurrentVisibleErrorUiInsteadOfConversationBodyText()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("const visible = element =>", source, StringComparison.Ordinal);
        Assert.Contains("const isCurrentTurnElement = element =>", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"alert\"]", source, StringComparison.Ordinal);
        Assert.Contains("[aria-live=\"assertive\"]", source, StringComparison.Ordinal);
        Assert.Contains("[data-testid*=\"error\"]", source, StringComparison.Ordinal);
        Assert.Contains("if (!visible(element) || !isCurrentTurnElement(element)) continue;", source, StringComparison.Ordinal);
        Assert.Contains("const isGenerating = !!stopButton;", source, StringComparison.Ordinal);
        Assert.Contains("const errorText = isGenerating ? '' : findErrorText();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const isGenerating = !!stopButton || streamingSignal;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[class*=\"error\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body?.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("visibleText.match", source, StringComparison.Ordinal);
    }
}
