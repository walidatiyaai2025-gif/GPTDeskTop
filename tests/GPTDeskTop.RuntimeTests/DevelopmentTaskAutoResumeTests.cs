using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskAutoResumeTests
{
    [Fact]
    public async Task PersistedWorkingStateAutoResumesWithoutRepeatingDeliveredMessage()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");
            var firstReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                first.MessageReady += message => firstReady.TrySetResult(message);
                await first.StartAsync("plan-1", "Plan One");
                var deliveredMessage = await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await first.CheckpointDeliveredAsync(
                    "monitor-1",
                    "tab-1",
                    DevelopmentTaskDeliveryCoordinator.Fingerprint(deliveredMessage));
                await first.MarkAwaitingAssistantResponseAsync(["monitor-1"]);
                Assert.True(first.State.AwaitingAssistantResponse);
                Assert.Equal(0, first.State.CurrentMessageIndex);
                Assert.Equal(DevelopmentTaskEngineStatus.Working, first.State.Status);
            }

            var restartedEmissions = 0;
            await using (var restarted = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                restarted.MessageReady += _ => Interlocked.Increment(ref restartedEmissions);
                Assert.True(await restarted.ResumeIfActiveAsync());
                await Task.Delay(500);

                Assert.Equal(DevelopmentTaskEngineStatus.Working, restarted.State.Status);
                Assert.True(restarted.State.AwaitingAssistantResponse);
                Assert.Equal(0, restarted.State.CurrentMessageIndex);
                Assert.Equal(0, Volatile.Read(ref restartedEmissions));

                Assert.True(await restarted.HandleAssistantResponseAsync(
                    "monitor-1", "completed after restart", isError: false));
                Assert.Equal(1, restarted.State.CurrentMessageIndex);
            }
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task PersistedPausedStateDoesNotAutoResume()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\"]}");

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan-1", "Plan One");
                await first.PauseAsync();
                Assert.Equal(DevelopmentTaskEngineStatus.Paused, first.State.Status);
            }

            var emissions = 0;
            await using (var restarted = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                restarted.MessageReady += _ => Interlocked.Increment(ref emissions);
                Assert.False(await restarted.ResumeIfActiveAsync());
                await Task.Delay(250);

                Assert.Equal(DevelopmentTaskEngineStatus.Paused, restarted.State.Status);
                Assert.Equal(0, Volatile.Read(ref emissions));
            }
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task PersistedStoppedStateDoesNotAutoResume()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\"]}");

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan-1", "Plan One");
                await first.StopAsync();
                Assert.Equal(DevelopmentTaskEngineStatus.Stopped, first.State.Status);
            }

            await using var restarted = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath);
            Assert.False(await restarted.ResumeIfActiveAsync());
            Assert.Equal(DevelopmentTaskEngineStatus.Stopped, restarted.State.Status);
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }
}
