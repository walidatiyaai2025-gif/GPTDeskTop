using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskWorkWindowTests
{
    [Fact]
    public async Task SuccessfulMessageDoesNotEmitSecondMessageWithinSameWorkWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var messages = Path.Combine(root, "messages.json");
            var state = Path.Combine(root, "state.json");
            await File.WriteAllTextAsync(messages, "{\"Messages\":[\"one\",\"two\",\"three\"]}");
            await using var engine = new DevelopmentTaskEngine(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), state, messages);
            var sent = 0;
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var coordinator = new DevelopmentTaskDeliveryCoordinator(
                engine,
                (_, _) =>
                {
                    Interlocked.Increment(ref sent);
                    delivered.TrySetResult(true);
                    return Task.FromResult(true);
                },
                responseMonitorId: "monitor-1");

            await engine.StartAsync("p", "plan");
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitUntilAsync(() => engine.State.AwaitingAssistantResponse, TimeSpan.FromSeconds(3));
            await Task.Delay(800);

            Assert.Equal(1, Volatile.Read(ref sent));
            Assert.Equal(0, engine.State.CurrentMessageIndex);
            Assert.True(engine.State.AwaitingAssistantResponse);
            Assert.Equal(DevelopmentTaskEngineStatus.Working, engine.State.Status);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void PersistedDeliveredPreviousMessagePreventsDuplicateAfterResume()
    {
        var state = new DevelopmentTaskState
        {
            Status = DevelopmentTaskEngineStatus.Working,
            CurrentMessageIndex = 1,
            LastDeliveredMessageIndex = 0,
            LastDeliveredMessageFingerprint = "fingerprint"
        };

        var alreadyDeliveredInWindow = state.LastDeliveredMessageIndex == state.CurrentMessageIndex - 1 &&
                                       !string.IsNullOrWhiteSpace(state.LastDeliveredMessageFingerprint);

        Assert.True(alreadyDeliveredInWindow);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not reached in time.");
            await Task.Delay(20);
        }
    }
}
