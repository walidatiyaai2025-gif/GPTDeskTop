using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskDeliveryCoordinatorTests
{
    [Fact]
    public async Task FailedDeliveryDoesNotAdvanceTask()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"first\",\"second\"]}");
            await using var engine = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await engine.StartAsync("p", "Plan");

            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(engine, (_, _) =>
            {
                delivered.TrySetResult(true);
                return Task.FromResult(false);
            });

            await WaitForAsync(() => delivered.Task.IsCompleted);
            Assert.Equal(0, engine.State.CurrentMessageIndex);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task SuccessfulDeliveryAdvancesExactlyOnce()
    {
        var root = CreateRoot();
        try
        {
            var messagesPath = Path.Combine(root, "task-messages.json");
            var statePath = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messagesPath, "{\"Messages\":[\"first\",\"second\"]}");
            await using var engine = new DevelopmentTaskEngine(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), statePath, messagesPath);
            await engine.StartAsync("p", "Plan");

            var sendCount = 0;
            var advanced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(engine, (_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                advanced.TrySetResult(true);
                return Task.FromResult(true);
            });

            await WaitForAsync(() => advanced.Task.IsCompleted);
            await Task.Delay(350);

            Assert.Equal(1, engine.State.CurrentMessageIndex);
            Assert.Equal(1, sendCount);
        }
        finally { TryDelete(root); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }
}
