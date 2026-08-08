using GPTDeskTop.Services.DevelopmentTaskEngine;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskRecoveryTests
{
    [Fact]
    public async Task RestoresSameMonitorTabAndMessageIndexWithoutAdvancing()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"gptdesktop-recovery-{Guid.NewGuid():N}.json");
        var messagesPath = Path.Combine(Path.GetTempPath(), $"gptdesktop-messages-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(messagesPath, "{\"messages\":[\"one\",\"two\",\"three\"]}");
        try
        {
            var engine = new DevelopmentTaskEngine(statePath: statePath, messagesPath: messagesPath);
            engine.RestorePosition(1, 1, DevelopmentTaskEngineStatus.Working);
            await engine.CheckpointDeliveredAsync("monitor-7", "tab-11", "ABC123");

            var restarted = new DevelopmentTaskEngine(statePath: statePath, messagesPath: messagesPath);
            await restarted.ResumeAsync();
            await Task.Delay(50);

            var recovery = new DevelopmentTaskRecoveryService(restarted);
            var result = await recovery.RestoreAsync(
                restarted.State,
                "monitor-7",
                "tab-11",
                tabId => Task.FromResult(tabId == "tab-11"));

            Assert.True(result.Success);
            Assert.Equal("monitor-7", result.MonitorId);
            Assert.Equal("tab-11", result.TabId);
            Assert.Equal(1, result.MessageIndex);
            Assert.Equal(1, result.LastDeliveredMessageIndex);
            Assert.Equal("ABC123", result.LastDeliveredMessageFingerprint);
            Assert.Equal(1, restarted.State.CurrentMessageIndex);
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(messagesPath);
        }
    }

    [Fact]
    public async Task RejectsRecoveryWhenPersistedTabIsGone()
    {
        var engine = new DevelopmentTaskEngine();
        engine.RestorePosition(2, 2, DevelopmentTaskEngineStatus.Working);
        await engine.CheckpointDeliveredAsync("monitor-7", "tab-missing", "FP");

        var recovery = new DevelopmentTaskRecoveryService(engine);
        var result = await recovery.RestoreAsync(
            engine.State,
            "monitor-7",
            "tab-missing",
            _ => Task.FromResult(false));

        Assert.False(result.Success);
        Assert.Contains("no longer available", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
