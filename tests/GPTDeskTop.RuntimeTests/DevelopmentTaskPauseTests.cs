using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskPauseTests
{
    [Fact]
    public async Task PauseStopsDeliveryWorkerAndResumeRestartsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var messages = Path.Combine(root, "messages.json");
            var state = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messages, "{\"Messages\":[\"one\",\"two\"]}");

            await using var engine = new DevelopmentTaskEngine(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(1),
                state,
                messages);

            var sent = 0;
            engine.MessageReady += _ => Interlocked.Increment(ref sent);
            await engine.StartAsync("p", "plan");
            await Task.Delay(350);
            Assert.Equal(1, sent);

            await engine.PauseAsync();
            Assert.Equal(DevelopmentTaskEngineStatus.Paused, engine.State.Status);
            var pausedCount = sent;
            await Task.Delay(500);
            Assert.Equal(pausedCount, sent);

            await engine.ResumeAsync();
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
            await engine.StopAsync();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
