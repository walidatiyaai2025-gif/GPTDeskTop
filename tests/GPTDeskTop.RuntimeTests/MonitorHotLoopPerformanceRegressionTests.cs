namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorHotLoopPerformanceRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void StreamingPollDoesNotSerializeGrowingAssistantResponseBody()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        var generationCheck = source.IndexOf(
            "const isGenerating = !!stopButton || streamingSignal;",
            StringComparison.Ordinal);
        var responseRead = source.IndexOf(
            "const last = !isGenerating && messages.length",
            StringComparison.Ordinal);

        Assert.True(generationCheck >= 0);
        Assert.True(responseRead > generationCheck);
        Assert.Contains(
            "return { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText };",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "const last = messages.length ? (messages[messages.length - 1].innerText || '').trim() : '';",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeRotationSettingsAreCachedBetweenPollsAndRefreshWithinFiveSeconds()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains(
            "RuntimeSettingsRefreshInterval = TimeSpan.FromSeconds(5)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var nextRuntimeSettingsRefreshUtc = DateTimeOffset.UtcNow + RuntimeSettingsRefreshInterval;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (DateTimeOffset.UtcNow >= nextRuntimeSettingsRefreshUtc)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "nextRuntimeSettingsRefreshUtc = DateTimeOffset.UtcNow + RuntimeSettingsRefreshInterval;",
            source,
            StringComparison.Ordinal);

        Assert.Equal(
            2,
            source.Split("GetIntSettingAsync(\"RotateAfterAssistantMessages\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            source.Split("GetSettingAsync(\"MessageCountRotationStartMessage\"", StringSplitOptions.None).Length - 1);
    }
}