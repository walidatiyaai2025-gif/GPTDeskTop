using System.Collections.Concurrent;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.RuntimeTests;

public sealed class OutboundDeliveryWallClockAcceptanceTests
{
    [Fact]
    [Trait("Category", "WallClockAcceptance")]
    public async Task ProductionCoordinatorQueuesThreeSendsWithRealFiveSecondAuthorityGap()
    {
        var coordinator = new OutboundDeliveryCoordinator();
        var physicalSendUtc = new ConcurrentQueue<DateTimeOffset>();
        var receipts = new ConcurrentQueue<OutboundDeliveryReceipt>();
        var activity = new ConcurrentQueue<string>();
        coordinator.ReceiptReleased += receipts.Enqueue;

        var operations = Enumerable.Range(1, 3)
            .Select(index => coordinator.SendOnceAsync(
                index,
                $"chat-{index}",
                $"message-{index}",
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
        Assert.Equal(0, released[0].MeasuredGapMs);
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
    public void ProductionGapRemainsFiveSecondsAndPhysicalComposerHasOneCoordinatorCallSite()
    {
        var coordinatorSource = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");
        Assert.Contains("DefaultInterSendGap = TimeSpan.FromSeconds(5)", coordinatorSource, StringComparison.Ordinal);

        var monitorSource = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Equal(1, CountOccurrences(monitorSource, "SendChatMessageVerifiedAsync("));
        Assert.Equal(1, CountOccurrences(monitorSource, "_outboundDelivery.SendOnceAsync("));
        Assert.Contains(
            "() => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken)",
            monitorSource,
            StringComparison.Ordinal);

        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "GPTDeskTop"));
        var physicalCallers = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("ChromeDevToolsService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("SendChatMessageVerifiedAsync(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Services/ChatGptMonitorService.cs" }, physicalCallers);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
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
