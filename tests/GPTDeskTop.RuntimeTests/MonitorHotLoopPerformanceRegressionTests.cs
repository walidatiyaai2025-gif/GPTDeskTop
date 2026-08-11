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
            "state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText };",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "const last = messages.length ? (messages[messages.length - 1].innerText || '').trim() : '';",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStatePollingUsesDirtyObserverAndTinySteadyStateExpression()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains(
            "private const string ChatStateReadExpression = \"window.__gptDesktopChatStateCache?.version === 2 ? window.__gptDesktopChatStateCache.read() : null\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.observer = new MutationObserver(() => { state.dirty = true; });",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (!state.dirty) return state.snapshot;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.observer.observe(root",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var value = await EvaluateAsync(tab, ChatStateReadExpression, cancellationToken, false);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (value.ValueKind == JsonValueKind.Null)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "value = await EvaluateAsync(tab, ChatStateInstallExpression, cancellationToken, false);",
            source,
            StringComparison.Ordinal);

        var readExpressionStart = source.IndexOf("private const string ChatStateReadExpression", StringComparison.Ordinal);
        var installExpressionStart = source.IndexOf("private const string ChatStateInstallExpression", StringComparison.Ordinal);
        Assert.True(readExpressionStart >= 0 && installExpressionStart > readExpressionStart);
        Assert.True(installExpressionStart - readExpressionStart < 260);
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
