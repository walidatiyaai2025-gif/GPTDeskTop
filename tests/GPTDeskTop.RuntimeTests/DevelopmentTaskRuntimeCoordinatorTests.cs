using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskRuntimeCoordinatorTests
{
    [Fact]
    public async Task RepeatedStartDoesNotCreateAnotherLifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state.json");
        var messages = Path.Combine(root, "messages.json");
        await File.WriteAllTextAsync(messages, "{\"messages\":[\"step {step}\"]}");

        await using var engine = new DevelopmentTaskEngine(
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), state, messages);
        await using var coordinator = new DevelopmentTaskRuntimeCoordinator(engine);

        Assert.True(await coordinator.StartAsync("plan-1", "Plan 1"));
        Assert.False(await coordinator.StartAsync("plan-1", "Plan 1"));
        Assert.True(coordinator.IsStarted);

        await coordinator.StopAsync();
        Assert.False(coordinator.IsStarted);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task PersistedPositionIsResumedInsteadOfResettingToMessageZero()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state.json");
        var messages = Path.Combine(root, "messages.json");
        await File.WriteAllTextAsync(messages, "{\"messages\":[\"one\",\"two\",\"three\"]}");

        await using (var first = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), state, messages))
        {
            await first.StartAsync("plan-1", "Plan 1");
            await first.AdvanceAsync();
            await first.CheckpointDeliveredAsync("monitor-1", "tab-1", "fp-1");
        }

        await using var restored = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), state, messages);
        await using var coordinator = new DevelopmentTaskRuntimeCoordinator(restored);
        Assert.True(await coordinator.StartAsync("plan-1", "Plan 1"));
        Assert.Equal(1, restored.State.CurrentMessageIndex);
        Assert.Equal(1, restored.State.LastDeliveredMessageIndex);
        Assert.Equal("monitor-1", restored.State.LastMonitorId);
        Assert.Equal("tab-1", restored.State.LastTabId);
        Assert.Equal("fp-1", restored.State.LastDeliveredMessageFingerprint);

        await coordinator.StopAsync();
        Directory.Delete(root, recursive: true);
    }
}
