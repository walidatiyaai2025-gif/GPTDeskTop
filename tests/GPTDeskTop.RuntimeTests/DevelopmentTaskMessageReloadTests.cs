using System.Text.Json;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskMessageReloadTests
{
    [Fact]
    public async Task CatalogEditedDuringCooling_IsUsedByNextWorkWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var statePath = Path.Combine(root, "state.json");
        var messagesPath = Path.Combine(root, "messages.json");
        try
        {
            WriteCatalog(messagesPath, "first-version");
            var engine = new DevelopmentTaskEngine(
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(120),
                statePath,
                messagesPath);

            var firstMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coolingStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;

            engine.MessageReady += message =>
            {
                if (Interlocked.Increment(ref count) == 1) firstMessage.TrySetResult(message);
                else secondMessage.TrySetResult(message);
            };
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult(true);

            await engine.StartAsync("plan", "Reload test");
            var first = await firstMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains("first-version", first);

            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            WriteCatalog(messagesPath, "edited-during-cooling");

            var next = await secondMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains("edited-during-cooling", next);
            Assert.DoesNotContain("first-version", next);

            await engine.StopAsync();
            await engine.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CatalogCanGrowDuringCooling_WithoutRestartingEngine()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var statePath = Path.Combine(root, "state.json");
        var messagesPath = Path.Combine(root, "messages.json");
        try
        {
            WriteCatalog(messagesPath, "one");
            var engine = new DevelopmentTaskEngine(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                statePath,
                messagesPath);
            var coolingStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.CoolingStarted += (_, _) => coolingStarted.TrySetResult(true);

            await engine.StartAsync("plan", "Growth test");
            await coolingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            WriteCatalog(messagesPath, "one", "two", "three");

            await Task.Delay(250);
            Assert.Equal(3, engine.State.TotalMessages);
            await engine.StopAsync();
            await engine.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteCatalog(string path, params string[] messages)
    {
        var json = JsonSerializer.Serialize(new { messages }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
