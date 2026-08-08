using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskCheckpointTests
{
    [Fact]
    public async Task VerifiedDeliveryPersistsMonitorTabMessageIndexAndFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var messagesPath = Path.Combine(root, "task-messages.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"first\",\"second\"]}");

            await using var engine = new DevelopmentTaskEngine(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await engine.StartAsync("plan", "Plan");
            var fingerprint = DevelopmentTaskDeliveryCoordinator.Fingerprint("first");

            await engine.CheckpointDeliveredAsync("monitor-7", "tab-11", fingerprint);

            await using var resumed = new DevelopmentTaskEngine(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await resumed.ResumeAsync();

            Assert.Equal("monitor-7", resumed.State.LastMonitorId);
            Assert.Equal("tab-11", resumed.State.LastTabId);
            Assert.Equal(0, resumed.State.LastDeliveredMessageIndex);
            Assert.Equal(fingerprint, resumed.State.LastDeliveredMessageFingerprint);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
