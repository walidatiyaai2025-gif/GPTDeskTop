using System.Collections.Concurrent;
using GPTDeskTop.Runtime;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalOutboundSafetyRegressionTests
{
    [Fact]
    public async Task ThreeMonitors_AreSerializedInFifoOrder_WithFiveSecondGlobalGap()
    {
        var delays = new ConcurrentQueue<TimeSpan>();
        var coordinator = new OutboundDeliveryCoordinator(
            (delay, _) => { delays.Enqueue(delay); return Task.CompletedTask; });
        var order = new ConcurrentQueue<long>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinator.SendOnceAsync(1, "c1", "a", async () =>
        {
            order.Enqueue(1);
            firstStarted.TrySetResult();
            await releaseFirst.Task;
            return true;
        }, null, CancellationToken.None);
        await firstStarted.Task;

        var second = coordinator.SendOnceAsync(2, "c2", "b", () =>
        {
            order.Enqueue(2);
            return Task.FromResult(true);
        }, null, CancellationToken.None);
        await Task.Yield();
        var third = coordinator.SendOnceAsync(3, "c3", "c", () =>
        {
            order.Enqueue(3);
            return Task.FromResult(true);
        }, null, CancellationToken.None);

        Assert.Equal(2, coordinator.QueuedCount);
        releaseFirst.TrySetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.True(await third);
        Assert.Equal(new long[] { 1, 2, 3 }, order.ToArray());
        Assert.Equal(3, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(5), delay));
    }

    [Fact]
    public async Task PhysicalSendAuthority_IsNeverOwnedByTwoMonitorsAtOnce()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var active = 0;
        var maxActive = 0;
        async Task<bool> Send()
        {
            var now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return true;
        }

        await Task.WhenAll(
            coordinator.SendOnceAsync(11, "a", "one", Send, null, CancellationToken.None),
            coordinator.SendOnceAsync(12, "b", "two", Send, null, CancellationToken.None));
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task FailedMonitor_DoesNotDeadlockNextMonitor()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var failed = coordinator.SendOnceAsync(21, "a", "one",
            () => throw new IOException("simulated"), null, CancellationToken.None);
        var next = coordinator.SendOnceAsync(22, "b", "two",
            () => Task.FromResult(true), null, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => failed);
        Assert.True(await next);
    }

    [Fact]
    public async Task CancelledQueuedMonitor_IsRemovedWithoutBlockingFollower()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.SendOnceAsync(31, "a", "one", async () =>
        {
            await release.Task;
            return true;
        }, null, CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        var second = coordinator.SendOnceAsync(32, "b", "two", () => Task.FromResult(true), null, cancelled.Token);
        var third = coordinator.SendOnceAsync(33, "c", "three", () => Task.FromResult(true), null, CancellationToken.None);
        cancelled.Cancel();
        release.TrySetResult();
        Assert.True(await first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.True(await third);
    }

    [Fact]
    public async Task UncertainIdenticalSend_IsNotPhysicallyClickedTwice()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var physical = 0;
        Assert.False(await coordinator.SendOnceAsync(41, "same", "continue", () =>
        {
            Interlocked.Increment(ref physical);
            return Task.FromResult(false);
        }, null, CancellationToken.None));
        Assert.False(await coordinator.SendOnceAsync(41, "same", "continue", () =>
        {
            Interlocked.Increment(ref physical);
            return Task.FromResult(true);
        }, null, CancellationToken.None));
        Assert.Equal(1, physical);
    }

    [Theory]
    [InlineData("Too many requests")]
    [InlineData("You are making requests too quickly")]
    [InlineData("We have temporarily limited access to your conversations")]
    [InlineData("Please wait a few minutes before trying again")]
    public void RateLimitMarkers_AreDetectedCaseInsensitively(string text)
        => Assert.True(GlobalChatGptRateLimitCircuitBreaker.IsRateLimitText(text.ToUpperInvariant()));

    [Fact]
    public void GlobalRateLimit_BackoffProgressesFiveTenFifteenThirty_ThenClears()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);

        breaker.ObserveVisibleState("Too many requests");
        Assert.True(breaker.IsActive);
        Assert.Equal(1, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(5), breaker.RetryAtUtc);

        now = now.AddMinutes(5);
        breaker.ObserveVisibleState("temporarily limited access");
        Assert.Equal(2, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(10), breaker.RetryAtUtc);

        now = now.AddMinutes(10);
        breaker.ObserveVisibleState("making requests too quickly");
        Assert.Equal(3, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(15), breaker.RetryAtUtc);

        now = now.AddMinutes(15);
        breaker.ObserveVisibleState("Too many requests");
        Assert.Equal(4, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(30), breaker.RetryAtUtc);

        now = now.AddMinutes(30);
        breaker.ObserveVisibleState(string.Empty);
        Assert.False(breaker.IsActive);
        Assert.Equal(0, breaker.BackoffStep);
        Assert.Null(breaker.RetryAtUtc);
    }

    [Fact]
    public void ClearingBeforeCooldown_DoesNotPrematurelyResume()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);
        breaker.ObserveVisibleState("Too many requests");
        now = now.AddMinutes(4);
        breaker.ObserveVisibleState(string.Empty);
        Assert.True(breaker.IsActive);
        now = now.AddMinutes(1);
        breaker.ObserveVisibleState(string.Empty);
        Assert.False(breaker.IsActive);
    }

    [Fact]
    public async Task ConcurrentCooldownObservations_ProduceOneGlobalProbeDecision()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);
        breaker.ObserveVisibleState("Too many requests");
        now = now.AddMinutes(5);
        var transitions = 0;
        breaker.StatusChanged += status => { if (status.EventName == "RateLimitStillActive") Interlocked.Increment(ref transitions); };

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => breaker.ObserveVisibleState("Too many requests"))));

        Assert.Equal(1, transitions);
        Assert.Equal(2, breaker.BackoffStep);
    }

    [Fact]
    public void RecoveryRotationAndAutoReplyShareCanonicalQueueBackedSendMethod()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        Assert.Contains("RotateByMessageCountAsync", source, StringComparison.Ordinal);
        Assert.Contains("TimeoutRecoveryMessage", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptErrorContinuationMessage", source, StringComparison.Ordinal);
        Assert.Contains("var autoSent = await SendWhenReadyAsync", source, StringComparison.Ordinal);
        var method = source[source.IndexOf("private async Task<bool> SendWhenReadyAsync", StringComparison.Ordinal)..];
        Assert.Contains("_outboundDelivery.SendOnceAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageVerifiedAsync(tab, message", source[..source.IndexOf("private async Task<bool> SendWhenReadyAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GPTDeskTop.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
