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
        Assert.Contains("if (state.IsGenerating || string.IsNullOrWhiteSpace(text)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NoResponseRefreshSeconds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"NoResponseRefresh\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lastResponseActivity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCurrentStructuredErrorsDriveGenericRecovery()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("var isError = !string.IsNullOrWhiteSpace(state.ErrorText)", source, StringComparison.Ordinal);
        Assert.Contains("isError && IsDeliveryTimeout(text)", source, StringComparison.Ordinal);
        Assert.Contains("Error saved. Refreshing only this tab", source, StringComparison.Ordinal);
        Assert.Contains("IsConversationContextLimit(text)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsErrorResponse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStateDetectionUsesCurrentVisibleErrorUiInsteadOfConversationBodyText()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("const visible = element =>", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"alert\"]", source, StringComparison.Ordinal);
        Assert.Contains("[aria-live=\"assertive\"]", source, StringComparison.Ordinal);
        Assert.Contains("[data-testid*=\"error\"]", source, StringComparison.Ordinal);
        Assert.Contains("if (!visible(element)) continue;", source, StringComparison.Ordinal);
        Assert.Contains("streamingSignal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[class*=\"error\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body?.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("visibleText.match", source, StringComparison.Ordinal);
    }
}
