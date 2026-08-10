namespace GPTDeskTop.RuntimeTests;

public sealed class ChatMonitorRuntimeCleanupTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void StopClaimsCleanupBeforeCancellationAndDisposesAfterWorkerCompletion()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var stop = Slice(source, "public async Task StopMonitorAsync", "public async Task StopAllAsync");

        var ownershipIndex = stop.IndexOf("runtime.StopOwnsCleanup = true;", StringComparison.Ordinal);
        var cancelIndex = stop.IndexOf("runtime.Cancellation.Cancel();", StringComparison.Ordinal);
        var awaitIndex = stop.IndexOf("await runtime.Worker;", StringComparison.Ordinal);
        var disposeIndex = stop.IndexOf("runtime.Cancellation.Dispose();", StringComparison.Ordinal);

        Assert.True(ownershipIndex >= 0, "Stop must claim cleanup ownership while the runtime is still protected by the monitor lock.");
        Assert.True(cancelIndex > ownershipIndex, "Stop must claim cleanup ownership before cancellation can race worker finalization.");
        Assert.True(awaitIndex > cancelIndex, "Stop must await the worker after cancellation.");
        Assert.True(disposeIndex > awaitIndex, "The cancellation source must remain alive until the worker has completed.");
        Assert.Contains("ReferenceEquals(current, runtime)", stop, StringComparison.Ordinal);
        Assert.Contains("if (removed) RunningStateChanged?.Invoke();", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfTerminatingWorkerOwnsRemovalDisposalAndRunningStateNotification()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");

        Assert.Contains("MonitorRuntime? runtimeToDispose = null;", loop, StringComparison.Ordinal);
        Assert.Contains("current.Cancellation.Token == cancellationToken", loop, StringComparison.Ordinal);
        Assert.Contains("&& !current.StopOwnsCleanup", loop, StringComparison.Ordinal);
        Assert.Contains("runtimeToDispose = current;", loop, StringComparison.Ordinal);
        Assert.Contains("runtimeToDispose.Cancellation.Dispose();", loop, StringComparison.Ordinal);
        Assert.Contains("RunningStateChanged?.Invoke();", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCarriesExplicitStopCleanupOwnershipState()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("private sealed class MonitorRuntime", source, StringComparison.Ordinal);
        Assert.Contains("public CancellationTokenSource Cancellation { get; }", source, StringComparison.Ordinal);
        Assert.Contains("public Task Worker { get; }", source, StringComparison.Ordinal);
        Assert.Contains("public bool StopOwnsCleanup { get; set; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record MonitorRuntime", source, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
