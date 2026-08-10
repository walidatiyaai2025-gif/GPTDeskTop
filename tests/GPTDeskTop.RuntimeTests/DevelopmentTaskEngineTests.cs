using System.Text.Json;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskEngineTests
{
    [Fact]
    public async Task WorkingRestartDoesNotEmitASecondMessageForThePersistedWorkWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");
            var emissions = 0;
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                first.MessageReady += _ => Interlocked.Increment(ref emissions);
                await using var coordinator = new DevelopmentTaskDeliveryCoordinator(
                    first,
                    (_, _) => Task.FromResult(true),
                    async (message, cancellationToken) =>
                    {
                        await first.CheckpointDeliveredAsync(
                            "monitor-1",
                            "tab-1",
                            DevelopmentTaskDeliveryCoordinator.Fingerprint(message),
                            cancellationToken);
                        delivered.TrySetResult();
                    });

                await first.StartAsync("plan-1", "Plan One");
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await WaitUntilAsync(() => first.State.CurrentMessageIndex == 1, TimeSpan.FromSeconds(3));
            }

            await using (var restarted = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMinutes(1),
                coolingWindow: TimeSpan.FromMinutes(1),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                restarted.MessageReady += _ => Interlocked.Increment(ref emissions);
                await restarted.ResumeAsync();
                await Task.Delay(600);

                Assert.Equal(DevelopmentTaskEngineStatus.Working, restarted.State.Status);
                Assert.Equal(1, restarted.State.CurrentMessageIndex);
                Assert.Equal(1, Volatile.Read(ref emissions));
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task CoolingRestartWaitsForThePersistedCoolingWindowAndEmitsOnlyOnceAfterward()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");
            var emissions = 0;
            var coolingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMilliseconds(150),
                coolingWindow: TimeSpan.FromSeconds(2),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                first.MessageReady += _ => Interlocked.Increment(ref emissions);
                first.CoolingStarted += (_, _) => coolingStarted.TrySetResult();
                await first.StartAsync("plan-1", "Plan One");
                await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(1, Volatile.Read(ref emissions));
            }

            await using (var restarted = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(2),
                coolingWindow: TimeSpan.FromSeconds(2),
                statePath: statePath,
                messagesPath: messagesPath))
            {
                var resumedEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                restarted.MessageReady += _ =>
                {
                    Interlocked.Increment(ref emissions);
                    resumedEmission.TrySetResult();
                };

                await restarted.ResumeAsync();
                Assert.Equal(DevelopmentTaskEngineStatus.Cooling, restarted.State.Status);
                await Task.Delay(300);
                Assert.Equal(1, Volatile.Read(ref emissions));

                await resumedEmission.Task.WaitAsync(TimeSpan.FromSeconds(4));
                await Task.Delay(400);
                Assert.Equal(2, Volatile.Read(ref emissions));
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task CoolingStateIsPersistedAndCanResumeAfterRestartWithoutASecondWorker()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            var coolingStartedAt = DateTimeOffset.UtcNow;
            var persisted = new DevelopmentTaskState
            {
                PlanId = "plan-1",
                PlanTitle = "Plan One",
                CurrentMessageIndex = 1,
                CompletedMessages = 1,
                TotalMessages = 2,
                Status = DevelopmentTaskEngineStatus.Cooling,
                CoolingStartedAt = coolingStartedAt,
                LastCheckpointAt = coolingStartedAt,
                Revision = 7
            };
            await File.WriteAllTextAsync(
                statePath,
                JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true }));

            await using var resumed = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: TimeSpan.FromDays(1),
                statePath: statePath,
                messagesPath: messagesPath);

            await resumed.ResumeAsync();

            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, resumed.State.Status);
            Assert.NotNull(resumed.State.CoolingStartedAt);
            Assert.Equal(coolingStartedAt, resumed.State.CoolingStartedAt);
            Assert.Equal(1, resumed.State.CurrentMessageIndex);
            Assert.Equal(1, resumed.State.CompletedMessages);
            Assert.Equal(7, resumed.State.Revision);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Condition was not reached in time.");
            await Task.Delay(25);
        }
    }
}
