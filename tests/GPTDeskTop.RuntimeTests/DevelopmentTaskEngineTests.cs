using System.Text.Json;
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
}