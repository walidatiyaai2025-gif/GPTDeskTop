namespace GPTDeskTop.RuntimeTests;

public sealed class ChatStateGeneratingSignalRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void ChatStateUsesVisibleStopControlAsAuthoritativeGenerationSignal()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains(
            "const isGenerating = !!stopButton;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "const isGenerating = !!stopButton || streamingSignal;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStateCacheVersionBumpReinstallsTheCorrectedDetector()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains(
            "__gptDesktopChatStateCache?.version === 8",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const version = 8;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__gptDesktopChatStateCache?.version === 6",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedAssistantTextRemainsReadableWhenStreamingMarkersAreStale()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        var generationDecision = source.IndexOf(
            "const isGenerating = !!stopButton;",
            StringComparison.Ordinal);
        var completedTextRead = source.IndexOf(
            "const last = !isGenerating && lastAssistant ?",
            generationDecision,
            StringComparison.Ordinal);

        Assert.True(generationDecision >= 0);
        Assert.True(completedTextRead > generationDecision);
    }
}
