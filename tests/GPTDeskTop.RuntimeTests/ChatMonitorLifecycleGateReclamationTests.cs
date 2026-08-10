using System.Collections;
using System.Reflection;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChatMonitorLifecycleGateReclamationTests
{
    [Fact]
    public async Task SameMonitorWaiterKeepsGateAliveUntilBothLeasesRelease()
    {
        var service = CreateService();

        var first = await AcquireLeaseAsync(service, 41);
        var secondTask = BeginAcquireLease(service, 41);

        Assert.Equal(1, GateCount(service));
        Assert.Equal(2, GateReferenceCount(service, 41));
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, GateCount(service));
        Assert.Equal(1, GateReferenceCount(service, 41));

        second.Dispose();

        Assert.Equal(0, GateCount(service));
    }

    [Fact]
    public async Task DifferentMonitorIdsAcquireIndependentlyAndAreBothReclaimed()
    {
        var service = CreateService();

        var first = await AcquireLeaseAsync(service, 51);
        var second = await BeginAcquireLease(service, 52).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, GateCount(service));
        Assert.Equal(1, GateReferenceCount(service, 51));
        Assert.Equal(1, GateReferenceCount(service, 52));

        first.Dispose();
        Assert.Equal(1, GateCount(service));

        second.Dispose();
        Assert.Equal(0, GateCount(service));
    }

    [Fact]
    public async Task CompletedPublicTransitionsDoNotRetainHistoricalGateEntries()
    {
        var service = CreateService();

        for (var monitorId = 100L; monitorId < 125L; monitorId++)
            await service.StopMonitorAsync(monitorId);

        Assert.Equal(0, GateCount(service));
    }

    [Fact]
    public void LeaseContractRegistersBeforeWaitAndDisposesOnlyAtZeroReferences()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        var acquire = Slice(source, "private async Task<LifecycleGateLease> AcquireLifecycleGateAsync", "private void ReleaseLifecycleGate(");
        var release = Slice(source, "private void ReleaseLifecycleGate(", "private async Task MonitorLoopAsync");

        var incrementIndex = acquire.IndexOf("entry.ReferenceCount++;", StringComparison.Ordinal);
        var waitIndex = acquire.IndexOf("await entry.Gate.WaitAsync();", StringComparison.Ordinal);
        Assert.True(incrementIndex >= 0 && waitIndex > incrementIndex, "A waiter must register its reference before waiting on the semaphore.");

        Assert.Contains("entry.Gate.Release();", release, StringComparison.Ordinal);
        Assert.Contains("entry.ReferenceCount--;", release, StringComparison.Ordinal);
        Assert.Contains("entry.ReferenceCount == 0", release, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, entry)", release, StringComparison.Ordinal);
        Assert.Contains("_lifecycleGates.Remove(monitorId);", release, StringComparison.Ordinal);
        Assert.Contains("gateToDispose?.Dispose();", release, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _disposed, 1)", source, StringComparison.Ordinal);
    }

    private static ChatGptMonitorService CreateService()
    {
        var database = new LocalDatabase(Path.Combine(
            Path.GetTempPath(),
            $"gptdesktop-lifecycle-gate-{Guid.NewGuid():N}.db"));
        var chrome = new ChromeDevToolsService(new HttpClient(), new ChromeConfig());
        return new ChatGptMonitorService(chrome, database, new MonitoringConfig());
    }

    private static Task<IDisposable> BeginAcquireLease(ChatGptMonitorService service, long monitorId)
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "AcquireLifecycleGateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AcquireLifecycleGateAsync was not found.");

        var taskObject = method.Invoke(service, new object[] { monitorId })
            ?? throw new InvalidOperationException("AcquireLifecycleGateAsync returned null.");
        var task = (Task)taskObject;

        return AwaitLeaseAsync(taskObject, task);
    }

    private static async Task<IDisposable> AcquireLeaseAsync(ChatGptMonitorService service, long monitorId)
        => await BeginAcquireLease(service, monitorId);

    private static async Task<IDisposable> AwaitLeaseAsync(object taskObject, Task task)
    {
        await task;
        var result = taskObject.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(taskObject);
        return result as IDisposable
            ?? throw new InvalidOperationException("Lifecycle gate lease did not implement IDisposable.");
    }

    private static int GateCount(ChatGptMonitorService service)
        => LifecycleGateDictionary(service).Count;

    private static int GateReferenceCount(ChatGptMonitorService service, long monitorId)
    {
        var entry = LifecycleGateDictionary(service)[monitorId]
            ?? throw new InvalidOperationException($"Lifecycle gate entry {monitorId} was not found.");
        var property = entry.GetType().GetProperty("ReferenceCount", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Lifecycle gate reference count was not found.");
        return (int)(property.GetValue(entry) ?? 0);
    }

    private static IDictionary LifecycleGateDictionary(ChatGptMonitorService service)
    {
        var field = typeof(ChatGptMonitorService).GetField(
            "_lifecycleGates",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Lifecycle gate dictionary was not found.");
        return (IDictionary)(field.GetValue(service)
            ?? throw new InvalidOperationException("Lifecycle gate dictionary was null."));
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
