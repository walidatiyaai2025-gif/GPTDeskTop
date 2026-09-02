using GPTDeskTop.Runtime;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalChatTurnFenceTests
{
    [Fact]
    public void ActiveTurn_AllowsOnlyOwnerMonitor()
    {
        var fence = new GlobalChatTurnFence();

        fence.Activate(11, "test send");

        Assert.True(fence.CanRunMonitor(11));
        Assert.False(fence.CanRunMonitor(22));
        Assert.Equal(11, fence.ActiveMonitorId);
        Assert.Equal(GlobalChatTurnPhase.AwaitingResponse, fence.Snapshot().Phase);
    }

    [Fact]
    public async Task OperationGate_OtherMonitorWaitsOutsideSemaphoreUntilCooldownCompletes()
    {
        var fence = new GlobalChatTurnFence();
        var gate = new GlobalChatOperationGate(fence);
        fence.Activate(11, "verified send");

        var ownerLease = await gate.AcquireAsync(11, "owner-poll", CancellationToken.None);
        var waiter = gate.AcquireAsync(22, "queued-poll", CancellationToken.None);
        await Task.Delay(40);

        Assert.False(waiter.IsCompleted);
        Assert.Equal(11, gate.ActiveMonitorId);
        Assert.Equal(1, gate.QueuedCount);

        ownerLease.Dispose();
        Assert.True(fence.Complete(11, "stable response", TimeSpan.FromMilliseconds(80)));

        await Task.Delay(35);
        Assert.False(waiter.IsCompleted);
        Assert.Null(gate.ActiveMonitorId);

        using var next = await waiter.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(22, gate.ActiveMonitorId);
        Assert.Equal(0, gate.QueuedCount);
    }

    [Fact]
    public async Task OperationGate_ResponseOwnerCanPollWhileOtherMonitorIsParked()
    {
        var fence = new GlobalChatTurnFence();
        var gate = new GlobalChatOperationGate(fence);
        fence.Activate(101, "physical send attempted");

        var other = gate.AcquireAsync(202, "other-monitor", CancellationToken.None);
        await Task.Delay(30);
        Assert.False(other.IsCompleted);

        using (var owner = await gate.AcquireAsync(101, "authoritative-response-poll", CancellationToken.None))
        {
            Assert.Equal(101, gate.ActiveMonitorId);
        }

        Assert.False(other.IsCompleted);
        Assert.True(fence.Complete(101, "response complete", TimeSpan.Zero));
        using var released = await other.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(202, gate.ActiveMonitorId);
    }

    [Fact]
    public async Task PostResponseCooldown_BlocksSendAndDestructiveAutomationUntilExpiry()
    {
        var fence = GlobalChatTurnFence.Shared;
        var generation = GenerationRecoveryInterlock.Shared;
        fence.ResetForTests();
        generation.ResetForTests();
        try
        {
            fence.Activate(7, "send");
            Assert.True(fence.Complete(7, "stable response", TimeSpan.FromMilliseconds(90)));

            Assert.False(fence.CanAttemptSend(7, out var reason));
            Assert.Contains("post-response cooldown", reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(generation.IsActive(7));
            Assert.True(generation.HasAnyActiveLease);

            await Task.Delay(140);

            Assert.True(fence.CanAttemptSend(7, out _));
            Assert.False(generation.IsActive(7));
            Assert.False(generation.HasAnyActiveLease);
        }
        finally
        {
            fence.ResetForTests();
            generation.ResetForTests();
        }
    }

    [Fact]
    public void RuntimeSource_ActivatesTurnFenceOnlyForGateGovernedSavedMonitorSend()
    {
        var root = FindRepositoryRoot();
        var outbound = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));
        var gate = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Runtime", "GlobalChatOperationGate.cs"));

        Assert.Contains("GlobalChatOperationGate.Shared.ActiveMonitorId == monitorId", outbound, StringComparison.Ordinal);
        Assert.Contains("GlobalChatTurnFence.Shared.CanAttemptSend", outbound, StringComparison.Ordinal);
        Assert.Contains("GlobalChatTurnFence.Shared.Activate", outbound, StringComparison.Ordinal);
        Assert.Contains("GlobalChatTurnFence.Shared.Complete", outbound, StringComparison.Ordinal);
        Assert.Contains("WaitUntilRunnableAsync", gate, StringComparison.Ordinal);
        Assert.Contains("before the semaphore is held", gate, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GPTDeskTop.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
