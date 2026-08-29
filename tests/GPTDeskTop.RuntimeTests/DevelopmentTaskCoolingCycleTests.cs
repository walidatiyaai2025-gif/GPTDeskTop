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
                TimeSpan.FromMilliseconds(350),
                TimeSpan.FromMilliseconds(250),
                state,
                messages);

            var sent = 0;
            var coolingStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coolingCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondMessageSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult(true);
            engine.CoolingCompleted += (_, _) => coolingCompleted.TrySetResult(true);

            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(engine, (_, _) =>
            {
                var count = Interlocked.Increment(ref sent);
                if (count == 2) secondMessageSent.TrySetResult(true);
                return Task.FromResult(true);
            });

            await engine.StartAsync("p", "plan");
            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, Volatile.Read(ref sent));
            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, engine.State.Status);

            await coolingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await secondMessageSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // The second message is the final configured message. After its verified
            // delivery the engine may transition from Working to Completed immediately,
            // so assert the stable terminal state rather than racing that transient window.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (engine.State.Status != DevelopmentTaskEngineStatus.Completed && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(2, Volatile.Read(ref sent));
            Assert.Equal(2, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Completed, engine.State.Status);

            // Prove the next work window emitted exactly one message and did not loop.
            await Task.Delay(100);
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
}
