namespace GPTDeskTop.RuntimeTests;

public sealed class LastReleaseMainGateTriggerTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void DevelopmentMessageReloadRunsForEveryMainPush()
    {
        var workflow = ReadSource(".github", "workflows", "development-message-reload.yml");
        var pushStart = workflow.IndexOf("  push:", StringComparison.Ordinal);
        var pullRequestStart = workflow.IndexOf("  pull_request:", StringComparison.Ordinal);

        Assert.True(pushStart >= 0);
        Assert.True(pullRequestStart > pushStart);

        var pushBlock = workflow[pushStart..pullRequestStart];
        Assert.Contains("branches: [ main ]", pushBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("paths:", pushBlock, StringComparison.Ordinal);
    }
}
