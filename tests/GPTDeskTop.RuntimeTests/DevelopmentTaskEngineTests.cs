using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskEngineTests
{
    [Fact]
    public async Task AdvanceOnlyMovesToNextMessageAfterExplicitAdvance()
    {
        var root = CreateTempRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"step one\",\"step two\"]}");

            await using var engine = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: TimeSpan.FromMilliseconds(50),
                statePath: statePath,
                messagesPath: messagesPath);

            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.MessageReady += (_, message) => ready.TrySetResult(message);

            await engine.StartAsync("plan-1", "Plan One");
            Assert.Equal("step one", await ready.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, engine.State.CurrentMessageIndex);

            await engine.AdvanceAsync();
            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(1, engine.State.CompletedMessages);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StateSurvivesEngineRestartAndResumeKeepsMessageIndex()
    {
        var root = CreateTempRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\",\"three\"]}");

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: TimeSpan.FromMilliseconds(50),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan-2", "Plan Two");
                await WaitForAsync(() => first.State.CurrentMessageIndex == 0);
                await first.AdvanceAsync();
                Assert.Equal(1, first.State.CurrentMessageIndex);
            }

            await using var resumed = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: TimeSpan.FromMilliseconds(50),
                statePath: statePath,
                messagesPath: messagesPath);
            await resumed.ResumeAsync();

            Assert.Equal(1, resumed.State.CurrentMessageIndex);
            Assert.Equal("plan-2", resumed.State.PlanId);
            Assert.Equal("Plan Two", resumed.State.PlanTitle);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CoolingStateIsPersistedAndCanResumeAfterRestart()
    {
        var root = CreateTempRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMilliseconds(30),
                coolingWindow: TimeSpan.FromMilliseconds(300),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan-3", "Plan Three");
                await WaitForAsync(() => first.State.Status == DevelopmentTaskEngineStatus.Cooling, TimeSpan.FromSeconds(2));
                Assert.NotNull(first.State.CoolingStartedAt);
            }

            await using var resumed = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: TimeSpan.FromMilliseconds(300),
                statePath: statePath,
                messagesPath: messagesPath);
            await resumed.ResumeAsync();

            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, resumed.State.Status);
            Assert.NotNull(resumed.State.CoolingStartedAt);
            Assert.Equal(0, resumed.State.CurrentMessageIndex);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var limit = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= limit)
                throw new TimeoutException("Timed out waiting for runtime state transition.");
            await Task.Delay(10);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }
}
