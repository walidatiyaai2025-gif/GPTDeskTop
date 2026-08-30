using System.Text.Json;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskExplicitStartRegressionTests
{
    [Fact]
    public async Task ExplicitStartRestartsCompletedPlanAtPromptOne()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"messages\":[\"first\",\"second\"]}");
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new DevelopmentTaskState
            {
                PlanId = "default-development-plan",
                PlanTitle = "Development Plan",
                TotalMessages = 2,
                CurrentMessageIndex = 2,
                CompletedMessages = 2,
                Status = DevelopmentTaskEngineStatus.Completed
            }));

            await using var engine = new DevelopmentTaskEngine(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await using var runtime = new DevelopmentTaskRuntimeCoordinator(engine);
            var emitted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.MessageReady += message => emitted.TrySetResult(message);

            Assert.True(await runtime.StartAsync("default-development-plan", "Development Plan"));
            var message = await emitted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.Equal(0, engine.State.CompletedMessages);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
            Assert.Contains("first", message, StringComparison.Ordinal);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task PauseThenExplicitStartIsNotSuppressedByCoordinatorFlag()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"messages\":[\"first\",\"second\"]}");

            await using var engine = new DevelopmentTaskEngine(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await using var runtime = new DevelopmentTaskRuntimeCoordinator(engine);

            Assert.True(await runtime.StartAsync("default-development-plan", "Development Plan"));
            await runtime.PauseAsync();
            Assert.False(runtime.IsStarted);
            Assert.Equal(DevelopmentTaskEngineStatus.Paused, engine.State.Status);

            Assert.True(await runtime.StartAsync("default-development-plan", "Development Plan"));
            Assert.True(runtime.IsStarted);
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ProductionBindingPreflightsDevelopmentMessageOwnership()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskRuntimeBinding.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("ResolveEnabledRecipientsAsync", source, StringComparison.Ordinal);
        Assert.Contains("no eligible monitor is opted in", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_runtime.PauseAsync", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.ResumeAsync", source, StringComparison.Ordinal);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch { }
    }
}
