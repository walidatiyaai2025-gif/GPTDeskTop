using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var authorityGap12 = (released[1].SendAuthorityUtc - released[0].SendAuthorityUtc).TotalMilliseconds;
        var authorityGap23 = (released[2].SendAuthorityUtc - released[1].SendAuthorityUtc).TotalMilliseconds;
        var minimumAuthorityGap = Math.Min(authorityGap12, authorityGap23);
        Assert.True(minimumAuthorityGap >= 5000, $"Minimum production send-authority gap was {minimumAuthorityGap:0.###} ms.");

        WriteReceipt("send-gap-receipt.json", new
        {
            sourceSha = SourceSha(),
            operation1Id = released[0].OperationId,
            operation1MonitorId = released[0].MonitorId,
            operation1SendAuthorityUtc = released[0].SendAuthorityUtc,
            operation2Id = released[1].OperationId,
            operation2MonitorId = released[1].MonitorId,
            operation2SendAuthorityUtc = released[1].SendAuthorityUtc,
            operation3Id = released[2].OperationId,
            operation3MonitorId = released[2].MonitorId,
            operation3SendAuthorityUtc = released[2].SendAuthorityUtc,
            gap12Milliseconds = authorityGap12,
            gap23Milliseconds = authorityGap23,
            minimumGapMilliseconds = minimumAuthorityGap,
            requiredMinimumMilliseconds = 5000,
            passed = true
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

        var developmentBridge = ReadSource("src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "MonitorDevelopmentTaskBridge.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", developmentBridge, StringComparison.Ordinal);

        var targetFactory = ReadSource("src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskMonitorTargetFactory.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", targetFactory, StringComparison.Ordinal);

        var newChatWorkflow = ReadSource("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", newChatWorkflow, StringComparison.Ordinal);

        var projectExecution = ReadSource("src", "GPTDeskTop", "Services", "ProjectExecutionController.cs");
        Assert.Contains("_outboundDelivery.SendOnceAsync(", projectExecution, StringComparison.Ordinal);

        var startupResume = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        Assert.Contains("outboundDelivery.SendOnceAsync(", startupResume, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", startupResume, StringComparison.Ordinal);

        var tabRecovery = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");
        Assert.Contains("outboundDelivery.SendOnceAsync(", tabRecovery, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", tabRecovery, StringComparison.Ordinal);

        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "GPTDeskTop"));
        var physicalCallPattern = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\.SendChatMessageVerifiedAsync\(", RegexOptions.CultureInvariant);
        var physicalCallers = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("ChromeDevToolsService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => physicalCallPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var bypasses = physicalCallers
            .Where(path => !string.Equals(path, "Services/CrashRecoveryService.cs", StringComparison.Ordinal))
            .Where(path => !File.ReadAllText(Path.Combine(sourceRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                .Contains("SendOnceAsync(", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            bypasses.Length == 0,
            $"Physical send bypasses without canonical coordinator: {string.Join(", ", bypasses)}. All physical callers: {string.Join(", ", physicalCallers)}");
    }

    private static void WriteReceipt(string fileName, object receipt)
    {
        var directory = Environment.GetEnvironmentVariable("GPTDESKTOP_RUNTIME_CLOSURE_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string SourceSha()
        => Environment.GetEnvironmentVariable("GPTDESKTOP_RUNTIME_CLOSURE_SOURCE_SHA") ?? "LOCAL";

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
