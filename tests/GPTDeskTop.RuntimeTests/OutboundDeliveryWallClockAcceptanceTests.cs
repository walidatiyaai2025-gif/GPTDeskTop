using System.Collections.Concurrent;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.RuntimeTests;

public sealed class OutboundDeliveryWallClockAcceptanceTests
{
    [Fact]
    [Trait("Category", "WallClockAcceptance")]
    public async Task ThreeIndependentCoordinatorsShareOneRealFiveSecondGlobalAuthority()
    {
        var coordinators = new[]
        {
            new OutboundDeliveryCoordinator(),
            new OutboundDeliveryCoordinator(),
            new OutboundDeliveryCoordinator()
        };
        var physicalSendUtc = new ConcurrentQueue<DateTimeOffset>();
        var receipts = new ConcurrentQueue<OutboundDeliveryReceipt>();
        var activity = new ConcurrentQueue<string>();
        foreach (var coordinator in coordinators)
            coordinator.ReceiptReleased += receipts.Enqueue;

        var operations = coordinators.Select((coordinator, index) => coordinator.SendOnceAsync(
                index + 1,
                $"chat-{index + 1}",
                $"message-{index + 1}",
                () =>
                {
                    physicalSendUtc.Enqueue(DateTimeOffset.UtcNow);
                    return Task.FromResult(true);
                },
                activity.Enqueue,
                CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(operations);
        Assert.All(results, result => Assert.True(result));

        var times = physicalSendUtc.ToArray();
        Assert.Equal(3, times.Length);
        var firstGapMs = (times[1] - times[0]).TotalMilliseconds;
        var secondGapMs = (times[2] - times[1]).TotalMilliseconds;
        Assert.True(firstGapMs >= 5000, $"First real send gap was {firstGapMs:0.###} ms.");
        Assert.True(secondGapMs >= 5000, $"Second real send gap was {secondGapMs:0.###} ms.");

        var released = receipts.OrderBy(receipt => receipt.SendAuthorityUtc).ToArray();
        Assert.Equal(3, released.Length);
        Assert.True(released[1].MeasuredGapMs >= 5000, $"Receipt gap 2 was {released[1].MeasuredGapMs} ms.");
        Assert.True(released[2].MeasuredGapMs >= 5000, $"Receipt gap 3 was {released[2].MeasuredGapMs} ms.");
        Assert.All(released, receipt =>
        {
            Assert.False(string.IsNullOrWhiteSpace(receipt.OperationId));
            Assert.True(receipt.EnqueueUtc <= receipt.SendAuthorityUtc);
            Assert.True(receipt.SendAuthorityUtc <= receipt.ReleaseUtc);
            Assert.True(receipt.NextSendUtc <= receipt.ReleaseUtc + TimeSpan.FromMilliseconds(250));
        });

        var machineReadable = activity.Where(line => line.StartsWith("GlobalSendReceipt|{", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, machineReadable.Length);
        Assert.All(machineReadable, line =>
        {
            Assert.Contains("\"OperationId\"", line, StringComparison.Ordinal);
            Assert.Contains("\"MonitorId\"", line, StringComparison.Ordinal);
            Assert.Contains("\"EnqueueUtc\"", line, StringComparison.Ordinal);
            Assert.Contains("\"SendAuthorityUtc\"", line, StringComparison.Ordinal);
            Assert.Contains("\"ReleaseUtc\"", line, StringComparison.Ordinal);
            Assert.Contains("\"NextSendUtc\"", line, StringComparison.Ordinal);
            Assert.Contains("\"MeasuredGapMs\"", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EveryProductionPhysicalSendPathIsWrappedByTheCanonicalCoordinator()
    {
        var coordinatorSource = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");
        Assert.Contains("DefaultInterSendGap = TimeSpan.FromSeconds(5)", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("GlobalFifoSendAuthority GlobalAuthority", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("GlobalAuthority.AcquireAsync", coordinatorSource, StringComparison.Ordinal);

        var monitorSource = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", monitorSource, StringComparison.Ordinal);
        Assert.Contains("() => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken)", monitorSource, StringComparison.Ordinal);

        var recoveryAdapter = ReadSource("src", "GPTDeskTop", "Services", "ICrashRecoveryRuntime.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", recoveryAdapter, StringComparison.Ordinal);
        Assert.Contains("() => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken)", recoveryAdapter, StringComparison.Ordinal);

        var recoveryService = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryService.cs");
        Assert.Contains("runtime.SendChatMessageVerifiedAsync(tab, message, cancellationToken)", recoveryService, StringComparison.Ordinal);

        var developmentBridge = ReadSource("src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "MonitorDevelopmentTaskBridge.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", developmentBridge, StringComparison.Ordinal);
        Assert.Contains("() => _chrome.SendChatMessageVerifiedAsync(_tab, message, cancellationToken)", developmentBridge, StringComparison.Ordinal);

        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "GPTDeskTop"));
        var directChromeCallers = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("ChromeDevToolsService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("_chrome.SendChatMessageVerifiedAsync(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Services/ChatGptMonitorService.cs",
                "Services/DevelopmentTaskEngine/MonitorDevelopmentTaskBridge.cs",
                "Services/ICrashRecoveryRuntime.cs"
            }.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            directChromeCallers);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
