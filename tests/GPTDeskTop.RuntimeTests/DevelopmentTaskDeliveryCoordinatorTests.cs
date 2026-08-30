using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskDeliveryCoordinatorTests
{
    [Fact]
    public async Task FailedDeliveryDoesNotAdvanceTask()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"first\",\"second\"]}");
            await using var engine = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);

            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(engine, (_, _) =>
            {
                delivered.TrySetResult(true);
                return Task.FromResult(false);
            });

            await engine.StartAsync("p", "Plan");
            await WaitForAsync(() => delivered.Task.IsCompleted);
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.False(engine.State.AwaitingAssistantResponse);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task SuccessfulDeliveryWaitsForAssistantAndThenAdvancesExactlyOnce()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"first\",\"second\"]}");
            await using var engine = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);

            var sendCount = 0;
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(
                engine,
                (_, _) =>
                {
                    Interlocked.Increment(ref sendCount);
                    delivered.TrySetResult(true);
                    return Task.FromResult(true);
                },
                responseMonitorId: "17");

            await engine.StartAsync("p", "Plan");
            await WaitForAsync(() => delivered.Task.IsCompleted && engine.State.AwaitingAssistantResponse);
            await Task.Delay(150);

            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.Equal(0, engine.State.CompletedMessages);
            Assert.Equal(1, sendCount);
            Assert.True(engine.State.AwaitingAssistantResponse);
            Assert.Contains("17", engine.State.AwaitingResponseMonitorIds);

            var advanced = await engine.HandleAssistantResponseAsync("17", "stable completed response", isError: false);
            Assert.True(advanced);
            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(1, engine.State.CompletedMessages);
            Assert.False(engine.State.AwaitingAssistantResponse);

            await Task.Delay(200);
            Assert.Equal(1, sendCount);
        }
        finally { TryDelete(root); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }
}
