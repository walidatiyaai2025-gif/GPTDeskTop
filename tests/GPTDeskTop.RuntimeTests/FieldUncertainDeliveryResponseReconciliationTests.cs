using System.Collections;
using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class FieldUncertainDeliveryResponseReconciliationTests
{
    [Fact]
    public async Task UncertainSendBecomesCompletedAfterResponseEvidenceAndNextContinuationCanProceed()
    {
        var coordinator = CreateCoordinator();
        var physicalSendCount = 0;

        var firstAccepted = await SendAsync(
            coordinator,
            monitorId: 3,
            conversationKey: "field-conversation",
            message: "كمل",
            physicalSend: () =>
            {
                physicalSendCount++;
                return Task.FromResult(false);
            });

        Assert.False(firstAccepted);
        Assert.Equal(1, physicalSendCount);
        Assert.Equal("ReconcileRequired", State(coordinator, 3).Phase);

        MarkCompleted(coordinator, 3);

        var reconciled = State(coordinator, 3);
        Assert.Equal("Completed", reconciled.Phase);
        Assert.Equal("response-observed-after-uncertain-send", reconciled.Reason);

        var nextAccepted = await SendAsync(
            coordinator,
            monitorId: 3,
            conversationKey: "field-conversation",
            message: "كمل",
            physicalSend: () =>
            {
                physicalSendCount++;
                return Task.FromResult(true);
            });

        Assert.True(nextAccepted);
        Assert.Equal(2, physicalSendCount);
        Assert.Equal("Accepted", State(coordinator, 3).Phase);
    }

    [Fact]
    public async Task UncertainSendRemainsDuplicateSuppressedWithoutResponseEvidence()
    {
        var coordinator = CreateCoordinator();
        var physicalSendCount = 0;

        var firstAccepted = await SendAsync(
            coordinator,
            3,
            "field-conversation",
            "كمل",
            () =>
            {
                physicalSendCount++;
                return Task.FromResult(false);
            });

        var duplicateAccepted = await SendAsync(
            coordinator,
            3,
            "field-conversation",
            "كمل",
            () =>
            {
                physicalSendCount++;
                return Task.FromResult(true);
            });

        Assert.False(firstAccepted);
        Assert.False(duplicateAccepted);
        Assert.Equal(1, physicalSendCount);
        Assert.Equal("ReconcileRequired", State(coordinator, 3).Phase);
    }

    [Fact]
    public async Task MarkCompletedCannotReleaseLiveSendingOperation()
    {
        var coordinator = CreateCoordinator();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendTask = SendAsync(
            coordinator,
            3,
            "field-conversation",
            "كمل",
            () =>
            {
                entered.TrySetResult(true);
                return release.Task;
            });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("Sending", State(coordinator, 3).Phase);

        MarkCompleted(coordinator, 3);
        Assert.Equal("Sending", State(coordinator, 3).Phase);

        release.SetResult(true);
        Assert.True(await sendTask);
        Assert.Equal("Accepted", State(coordinator, 3).Phase);
    }

    [Fact]
    public void MonitorUsesOnlyStableNonErrorAssistantResponseAsReconciliationEvidence()
    {
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains(
            "if (!isError) _outboundDelivery.MarkCompleted(monitor.Id);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (isError) _outboundDelivery.MarkCompleted(monitor.Id);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;",
            source,
            StringComparison.Ordinal);
    }

    private static object CreateCoordinator()
    {
        var type = typeof(ChatGptMonitorService).Assembly.GetType(
            "GPTDeskTop.Runtime.OutboundDeliveryCoordinator",
            throwOnError: true)!;
        return Activator.CreateInstance(type, nonPublic: true)!;
    }

    private static async Task<bool> SendAsync(
        object coordinator,
        long monitorId,
        string conversationKey,
        string message,
        Func<Task<bool>> physicalSend)
    {
        var method = coordinator.GetType().GetMethod(
            "SendOnceAsync",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(coordinator.GetType().FullName, "SendOnceAsync");

        var task = Assert.IsAssignableFrom<Task<bool>>(method.Invoke(
            coordinator,
            new object?[]
            {
                monitorId,
                conversationKey,
                message,
                physicalSend,
                null,
                CancellationToken.None
            }));
        return await task;
    }

    private static void MarkCompleted(object coordinator, long monitorId)
    {
        var method = coordinator.GetType().GetMethod(
            "MarkCompleted",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(coordinator.GetType().FullName, "MarkCompleted");
        method.Invoke(coordinator, new object[] { monitorId });
    }

    private static (string Phase, string Reason) State(object coordinator, long monitorId)
    {
        var method = coordinator.GetType().GetMethod(
            "Snapshot",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(coordinator.GetType().FullName, "Snapshot");
        var snapshots = Assert.IsAssignableFrom<IEnumerable>(method.Invoke(coordinator, null));
        var snapshot = snapshots.Cast<object>().Single(item =>
            Convert.ToInt64(item.GetType().GetProperty("MonitorId")!.GetValue(item)) == monitorId);

        return (
            snapshot.GetType().GetProperty("Phase")!.GetValue(snapshot)!.ToString()!,
            (string)snapshot.GetType().GetProperty("Reason")!.GetValue(snapshot)!);
    }

    private static string NormalizeWhitespace(string source)
        => string.Join(
            " ",
            source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
