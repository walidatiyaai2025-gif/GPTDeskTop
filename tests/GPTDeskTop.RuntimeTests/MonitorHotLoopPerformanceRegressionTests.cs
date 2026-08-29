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
            "const isGenerating = !!stopButton;",
            StringComparison.Ordinal);
        var responseRead = source.IndexOf(
            "const last = !isGenerating && lastAssistant",
            StringComparison.Ordinal);

        Assert.True(generationCheck >= 0);
        Assert.True(responseRead > generationCheck);
        Assert.Contains(
            "state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText, globalRateLimitText, autoFollow:",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.autoFollow?.snapshot?.()",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "const streamingSignal = hasStreamingSignal(lastAssistant);",
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
            "private const string ChatStateReadExpression = \"window.__gptDesktopChatStateCache?.version === 8 ? window.__gptDesktopChatStateCache.read() : null\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.observer = new MutationObserver(() => { state.dirty = true; state.autoFollow?.onMutation?.(); });",
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
            "value = await EvaluateAsync(tab, BuildChatStateInstallExpression(), cancellationToken, false);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private const string ChatStateInstallExpressionTemplate = \"\"\"",
            source,
            StringComparison.Ordinal);

        var readExpressionStart = source.IndexOf("private const string ChatStateReadExpression", StringComparison.Ordinal);
        var installExpressionStart = source.IndexOf("private const string ChatStateInstallExpressionTemplate", StringComparison.Ordinal);
        Assert.True(readExpressionStart >= 0 && installExpressionStart > readExpressionStart);
        Assert.True(installExpressionStart - readExpressionStart < 280);
    }

    [Fact]
    public void RuntimeRotationSettingsAreSharedAcrossWorkersAndRefreshWithinFiveSeconds()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("RuntimeSettingsRefreshInterval = TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _runtimeSettingsRefreshGate = new(1, 1);", source, StringComparison.Ordinal);
        Assert.Contains("private RuntimeSettingsSnapshot? _runtimeSettingsSnapshot;", source, StringComparison.Ordinal);
        Assert.Contains("GetRuntimeSettingsSnapshotAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("await _runtimeSettingsRefreshGate.WaitAsync(cancellationToken);", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _runtimeSettingsSnapshot)", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Write(ref _runtimeSettingsSnapshot, snapshot);", source, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(rotateTask, messageTask)", source, StringComparison.Ordinal);
        Assert.Contains("ExpiresUtc", source, StringComparison.Ordinal);
        Assert.Contains("if (DateTimeOffset.UtcNow >= nextRuntimeSettingsRefreshUtc)", source, StringComparison.Ordinal);

        Assert.Equal(
            1,
            source.Split("\"RotateAfterAssistantMessages\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            source.Split("\"MessageCountRotationStartMessage\"", StringSplitOptions.None).Length - 1);
    }
}
