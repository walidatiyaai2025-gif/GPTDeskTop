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

            var emitted = new List<string>();
            engine.MessageReady += (_, message) => emitted.Add(message);

            await engine.StartAsync("plan", "Development Plan");
            await Task.Delay(700);

            Assert.Single(emitted);
            Assert.Equal("message-1", emitted[0]);

            await engine.AdvanceAsync();
            await Task.Delay(350);

            Assert.Equal(2, emitted.Count);
            Assert.Equal("next-2", emitted[1]);
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
