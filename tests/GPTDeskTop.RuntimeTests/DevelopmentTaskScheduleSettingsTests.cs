using System.Text.Json;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskScheduleSettingsTests
{
    [Fact]
    public void DefaultsRemainTenMinutesWorkAndFiveMinutesCooling()
    {
        var settings = new DevelopmentTaskScheduleSettings();
        settings.Validate();
        Assert.Equal(10, settings.WorkMinutes);
        Assert.Equal(5, settings.CoolingMinutes);
    }

    [Fact]
    public void InvalidValuesAreRejected()
    {
        var settings = new DevelopmentTaskScheduleSettings { WorkMinutes = 0, CoolingMinutes = 5 };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings = new DevelopmentTaskScheduleSettings { WorkMinutes = 10, CoolingMinutes = 121 };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void StoreRoundTripsConfiguredSchedule()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gptdesktop-schedule-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DevelopmentTaskScheduleSettingsStore(path);
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 7, CoolingMinutes = 3 });
            var loaded = store.Load();
            Assert.Equal(7, loaded.WorkMinutes);
            Assert.Equal(3, loaded.CoolingMinutes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public async Task PersistedScheduleIsLoadedByNewEngineAfterRestart()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            var schedulePath = Path.Combine(root, "schedule.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            var store = new DevelopmentTaskScheduleSettingsStore(schedulePath);
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 7, CoolingMinutes = 3 });

            await using (var first = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath))
            {
                Assert.Equal(TimeSpan.FromMinutes(7), first.WorkWindow);
                Assert.Equal(TimeSpan.FromMinutes(3), first.CoolingWindow);
                await first.StartAsync("plan", "Plan");
                await first.StopAsync();
            }

            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 9, CoolingMinutes = 4 });

            await using var restarted = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath);

            Assert.Equal(TimeSpan.FromMinutes(9), restarted.WorkWindow);
            Assert.Equal(TimeSpan.FromMinutes(4), restarted.CoolingWindow);

            await restarted.ResumeAsync();

            Assert.Equal(TimeSpan.FromMinutes(9), restarted.WorkWindow);
            Assert.Equal(TimeSpan.FromMinutes(4), restarted.CoolingWindow);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, restarted.State.Status);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SavedScheduleReloadsForNextWorkWindowAfterCooling()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            var schedulePath = Path.Combine(root, "schedule.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");

            var store = new DevelopmentTaskScheduleSettingsStore(schedulePath);
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 1, CoolingMinutes = 1 });

            var coolingState = new DevelopmentTaskState
            {
                PlanId = "plan",
                PlanTitle = "Plan",
                CurrentMessageIndex = 0,
                CompletedMessages = 0,
                TotalMessages = 2,
                Status = DevelopmentTaskEngineStatus.Cooling,
                CoolingStartedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(58)
            };
            await File.WriteAllTextAsync(
                statePath,
                JsonSerializer.Serialize(coolingState, new JsonSerializerOptions { WriteIndented = true }));

            await using var engine = new DevelopmentTaskEngine(
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath);

            var coolingCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.CoolingCompleted += (_, _) => coolingCompleted.TrySetResult(true);

            await engine.ResumeAsync();
            store.Save(new DevelopmentTaskScheduleSettings { WorkMinutes = 7, CoolingMinutes = 3 });

            await coolingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
            Assert.Equal(TimeSpan.FromMinutes(7), engine.WorkWindow);
            Assert.Equal(TimeSpan.FromMinutes(3), engine.CoolingWindow);
            Assert.NotNull(engine.State.WorkWindowStartedAt);
            Assert.Null(engine.State.CoolingStartedAt);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExplicitRuntimeWindowOverridesAreNotReplacedByPersistedSchedule()
    {
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "messages.json");
            var schedulePath = Path.Combine(root, "schedule.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"one\",\"two\"]}");
            new DevelopmentTaskScheduleSettingsStore(schedulePath).Save(
                new DevelopmentTaskScheduleSettings { WorkMinutes = 10, CoolingMinutes = 5 });

            var workOverride = TimeSpan.FromSeconds(2);
            var coolingOverride = TimeSpan.FromSeconds(1);
            await using var engine = new DevelopmentTaskEngine(
                workWindow: workOverride,
                coolingWindow: coolingOverride,
                statePath: statePath,
                messagesPath: messagesPath,
                scheduleSettingsPath: schedulePath);

            await engine.StartAsync("plan", "Plan");
            Assert.Equal(workOverride, engine.WorkWindow);
            Assert.Equal(coolingOverride, engine.CoolingWindow);

            await engine.PauseAsync();
            await engine.ResumeAsync();

            Assert.Equal(workOverride, engine.WorkWindow);
            Assert.Equal(coolingOverride, engine.CoolingWindow);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only; a worker may still be unwinding cancellation on slow CI agents.
        }
    }
}
