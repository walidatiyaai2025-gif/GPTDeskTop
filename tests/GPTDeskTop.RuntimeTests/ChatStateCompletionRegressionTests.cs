namespace GPTDeskTop.RuntimeTests;

public sealed class ChatStateCompletionRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void ChatStateCacheUpgradeForcesExistingTabsOntoTheNewDetector()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("__gptDesktopChatStateCache?.version === 8", source, StringComparison.Ordinal);
        Assert.Contains("const version = 8;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__gptDesktopChatStateCache?.version === 6", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleStreamingMarkersCannotKeepACompletedReplyGenerating()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("const isGenerating = !!stopButton;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const streamingSignal = hasStreamingSignal(lastAssistant);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const isGenerating = !!stopButton || streamingSignal;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("element.closest('form')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityMutationsInvalidateTheCachedGenerationState()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("'class', 'style', 'hidden', 'aria-hidden'", source, StringComparison.Ordinal);
        Assert.Contains("const lastAssistant = messages.length ? messages[messages.length - 1] : null;", source, StringComparison.Ordinal);
        Assert.Contains("const isGenerating = !!stopButton;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StopButtonFallbackOnlyMatchesResponseGenerationControls()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("stop generating|stop responding|إيقاف الإنشاء|إيقاف الرد", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stop responding|stop|إيقاف/i", source, StringComparison.Ordinal);
    }
}



