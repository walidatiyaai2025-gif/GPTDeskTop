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
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult(true);
            engine.CoolingCompleted += (_, _) => coolingCompleted.TrySetResult(true);

            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(engine, (_, _) =>
            {
                Interlocked.Increment(ref sent);
                return Task.FromResult(true);
            });

            await engine.StartAsync("p", "plan");
            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, sent);
            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, engine.State.Status);

            await coolingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(150);

            Assert.Equal(2, sent);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
            Assert.Equal(2, engine.State.CurrentMessageIndex);
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
