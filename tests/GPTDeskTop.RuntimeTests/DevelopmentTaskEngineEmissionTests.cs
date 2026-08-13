using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskEngineEmissionTests
{
    [Fact]
    public async Task CurrentMessageIsEmittedOnceUntilExplicitAdvance()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"message-{step}\",\"next-{step}\"]}");

            await using var engine = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(2),
                coolingWindow: TimeSpan.FromMilliseconds(50),
                statePath: statePath,
                messagesPath: messagesPath);

            var firstEmission = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEmission = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var emissionCount = 0;
            engine.MessageReady += message =>
            {
                var count = Interlocked.Increment(ref emissionCount);
                if (count == 1) firstEmission.TrySetResult(message);
                else if (count == 2) secondEmission.TrySetResult(message);
            };

            await engine.StartAsync("plan", "Development Plan");
            Assert.Equal("message-1", await firstEmission.Task.WaitAsync(TimeSpan.FromSeconds(2)));

            await Task.Delay(700);
            Assert.Equal(1, Volatile.Read(ref emissionCount));

            await engine.AdvanceAsync();
            Assert.Equal("next-2", await secondEmission.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(2, Volatile.Read(ref emissionCount));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    [Fact]
    public async Task WorkWindowTransitionsToCoolingAndThenBackToWorking()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            await using var engine = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMilliseconds(120),
                coolingWindow: TimeSpan.FromMilliseconds(180),
                statePath: statePath,
                messagesPath: messagesPath);

            var coolingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var coolingCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult();
            engine.CoolingCompleted += (_, _) => coolingCompleted.TrySetResult();

            await engine.StartAsync("plan", "Plan");
            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, engine.State.Status);

            await coolingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }
}
