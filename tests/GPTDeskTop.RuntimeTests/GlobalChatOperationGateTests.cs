using GPTDeskTop.Runtime;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalChatOperationGateTests
{
    [Fact]
    public async Task TwoMonitorOperations_NeverOverlap()
    {
        var gate = new GlobalChatOperationGate();
        using var first = await gate.AcquireAsync(101, "first", CancellationToken.None);

        var secondTask = gate.AcquireAsync(202, "second", CancellationToken.None);
        await Task.Delay(30);

        Assert.False(secondTask.IsCompleted);
        Assert.Equal(101, gate.ActiveMonitorId);
        Assert.Equal(1, gate.QueuedCount);

        first.Dispose();
        using var second = await secondTask;

        Assert.Equal(202, gate.ActiveMonitorId);
        Assert.Equal(0, gate.QueuedCount);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotBlockFollower()
    {
        var gate = new GlobalChatOperationGate();
        using var first = await gate.AcquireAsync(1, "holder", CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        var secondTask = gate.AcquireAsync(2, "cancelled", cancelled.Token);
        var thirdTask = gate.AcquireAsync(3, "follower", CancellationToken.None);
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
        first.Dispose();
        using var third = await thirdTask;

        Assert.Equal(3, gate.ActiveMonitorId);
        Assert.Equal(0, gate.QueuedCount);
    }

    [Fact]
    public void SavedMonitorRuntime_UsesGlobalOperationGateAndFifteenSecondStableDwell()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains("MinimumStableSendDwell = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
        Assert.Contains("_chatOperationGate.AcquireAsync", source, StringComparison.Ordinal);
        Assert.Contains("WaitForStableSendWindowAsync", source, StringComparison.Ordinal);
        Assert.Contains("15-second pre-send dwell", source, StringComparison.Ordinal);

        var loopStart = source.IndexOf("while (await timer.WaitForNextTickAsync(cancellationToken))", StringComparison.Ordinal);
        var gateAcquire = source.IndexOf("_chatOperationGate.AcquireAsync", loopStart, StringComparison.Ordinal);
        var handoffResume = source.IndexOf("TryResumePendingConversationHandoffAsync", loopStart, StringComparison.Ordinal);
        Assert.True(gateAcquire >= 0 && handoffResume > gateAcquire,
            "The process-wide chat-operation lease must be acquired before any monitor recovery/handoff work in a poll cycle.");
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
