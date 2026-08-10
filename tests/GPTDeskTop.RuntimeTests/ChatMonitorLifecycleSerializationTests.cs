namespace GPTDeskTop.RuntimeTests;

public sealed class ChatMonitorLifecycleSerializationTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void StartAndStopUseTheSamePerMonitorLifecycleGateContract()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        var start = Slice(source, "public async Task StartMonitorAsync", "public async Task StopMonitorAsync");
        var stop = Slice(source, "public async Task StopMonitorAsync", "public async Task StopAllAsync");

        Assert.Contains("private readonly Dictionary<long, SemaphoreSlim> _lifecycleGates", source, StringComparison.Ordinal);
        Assert.Contains("var lifecycleGate = GetLifecycleGate(monitor.Id);", start, StringComparison.Ordinal);
        Assert.Contains("await lifecycleGate.WaitAsync();", start, StringComparison.Ordinal);
        Assert.Contains("lifecycleGate.Release();", start, StringComparison.Ordinal);
        Assert.Contains("var lifecycleGate = GetLifecycleGate(monitorId);", stop, StringComparison.Ordinal);
        Assert.Contains("await lifecycleGate.WaitAsync();", stop, StringComparison.Ordinal);
        Assert.Contains("lifecycleGate.Release();", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void StopWaitsForTheExistingWorkerBeforeMakingTheRuntimeRestartable()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var stop = Slice(source, "public async Task StopMonitorAsync", "public async Task StopAllAsync");

        var lookupIndex = stop.IndexOf("_running.TryGetValue(monitorId, out runtime)", StringComparison.Ordinal);
        var awaitIndex = stop.IndexOf("await runtime.Worker;", StringComparison.Ordinal);
        var removeIndex = stop.IndexOf("_running.Remove(monitorId);", StringComparison.Ordinal);

        Assert.True(lookupIndex >= 0, "Stop must read the existing runtime without removing it first.");
        Assert.True(awaitIndex > lookupIndex, "Stop must await the existing worker after cancellation.");
        Assert.True(removeIndex > awaitIndex, "The runtime must not become restartable until its worker has completed.");
        Assert.DoesNotContain("_running.Remove(monitorId, out runtime)", stop, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, runtime)", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleSerializationIsKeyedPerMonitorInsteadOfGlobal()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("private SemaphoreSlim GetLifecycleGate(long monitorId)", source, StringComparison.Ordinal);
        Assert.Contains("_lifecycleGates.TryGetValue(monitorId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly SemaphoreSlim _lifecycleGate =", source, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
