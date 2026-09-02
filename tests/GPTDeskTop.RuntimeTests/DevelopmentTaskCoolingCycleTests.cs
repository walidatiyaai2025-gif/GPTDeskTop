using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskCoolingCycleTests
{
    [Fact]
    public async Task CoolingCompletesAndNextWorkWindowEmitsExactlyOneNextMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var messages = Path.Combine(root, "messages.json");
            var state = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messages, "{\"Messages\":[\"one\",\"two\"]}");

            await using var engine = new DevelopmentTaskEngine(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(250),
                state,
                messages);

            var sent = 0;
            var firstMessageSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coolingStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coolingCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondMessageSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult(true);
            engine.CoolingCompleted += (_, _) => coolingCompleted.TrySetResult(true);

            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(
                engine,
                (_, _) =>
                {
                    var count = Interlocked.Increment(ref sent);
                    if (count == 1) firstMessageSent.TrySetResult(true);
                    if (count == 2) secondMessageSent.TrySetResult(true);
                    return Task.FromResult(true);
                },
                responseMonitorId: "monitor-1");

            await engine.StartAsync("p", "plan");
            await firstMessageSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => engine.State.AwaitingAssistantResponse, TimeSpan.FromSeconds(2));

            Assert.Equal(1, Volatile.Read(ref sent));
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);

            Assert.True(await engine.HandleAssistantResponseAsync("monitor-1", "answer one", isError: false));
            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, engine.State.Status);

            await coolingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await secondMessageSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => engine.State.AwaitingAssistantResponse, TimeSpan.FromSeconds(2));

            Assert.Equal(2, Volatile.Read(ref sent));
            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);

            Assert.True(await engine.HandleAssistantResponseAsync("monitor-1", "answer two", isError: false));
            Assert.Equal(2, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Completed, engine.State.Status);

            await Task.Delay(300);
            Assert.Equal(2, Volatile.Read(ref sent));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void CoolingStateCannotAuthorizeDelivery()
    {
        var state = new DevelopmentTaskState { Status = DevelopmentTaskEngineStatus.Cooling };
        Assert.NotEqual(DevelopmentTaskEngineStatus.Working, state.Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not reached in time.");
            await Task.Delay(20);
        }
    }
}
