using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskEngineTests
{
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
            var deterministicCoolingWindow = TimeSpan.FromDays(1);

            await using (var first = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromMilliseconds(30),
                coolingWindow: deterministicCoolingWindow,
                statePath: statePath,
                messagesPath: messagesPath))
            {
                await first.StartAsync("plan-1", "Plan One");
                await Task.Delay(60);
                await first.AdvanceAsync();
                await WaitForAsync(() => first.State.Status == DevelopmentTaskEngineStatus.Cooling);
                Assert.Equal(1, first.State.CurrentMessageIndex);
                Assert.NotNull(first.State.CoolingStartedAt);
            }

            await using var resumed = new DevelopmentTaskEngine(
                workWindow: TimeSpan.FromSeconds(5),
                coolingWindow: deterministicCoolingWindow,
                statePath: statePath,
                messagesPath: messagesPath);

            await resumed.ResumeAsync();

            Assert.Equal(DevelopmentTaskEngineStatus.Cooling, resumed.State.Status);
            Assert.NotNull(resumed.State.CoolingStartedAt);
            Assert.Equal(1, resumed.State.CurrentMessageIndex);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for Cooling state.");
            await Task.Delay(10);
        }
    }
}