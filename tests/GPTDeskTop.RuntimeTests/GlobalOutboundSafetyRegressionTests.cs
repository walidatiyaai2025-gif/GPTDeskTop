using GPTDeskTop.Runtime;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalOutboundSafetyRegressionTests
{
    [Fact]
    public async Task ThreeMonitors_AreSerializedInFifoOrder_WithFifteenSecondGlobalGap()
    {
        var entered = new List<long>();
        var released = new List<TimeSpan>();
        var clock = TimeSpan.Zero;
        var sync = new object();
        var coordinator = new OutboundDeliveryCoordinator(
            (delay, _) =>
            {
                lock (sync)
                {
                    released.Add(delay);
                    clock += delay;
                }
                return Task.CompletedTask;
            });

        var active = 0;
        var maxActive = 0;
        async Task<bool> Send(long id)
        {
            Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, Volatile.Read(ref active));
            lock (sync) entered.Add(id);
            await Task.Delay(5);
            Interlocked.Decrement(ref active);
            return true;
        }

        var first = coordinator.SendOnceAsync(1, "c1", "one", () => Send(1), null, CancellationToken.None);
        var second = coordinator.SendOnceAsync(2, "c2", "two", () => Send(2), null, CancellationToken.None);
        var third = coordinator.SendOnceAsync(3, "c3", "three", () => Send(3), null, CancellationToken.None);
        await Task.WhenAll(first, second, third);

        Assert.Equal(new long[] { 1, 2, 3 }, entered);
        Assert.Equal(1, maxActive);
        Assert.Equal(3, released.Count);
        Assert.All(released, gap => Assert.Equal(TimeSpan.FromSeconds(15), gap));
    }

    [Fact]
    public async Task PhysicalSendAuthority_IsNeverOwnedByTwoMonitorsAtOnce()
    {
        var active = 0;
        var maxActive = 0;
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var tasks = Enumerable.Range(1, 20).Select(id => coordinator.SendOnceAsync(
            id,
            $"conversation-{id}",
            $"message-{id}",
            async () =>
            {
                var current = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, current);
                await Task.Delay(2);
                Interlocked.Decrement(ref active);
                return true;
            },
            null,
            CancellationToken.None));

        await Task.WhenAll(tasks);
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task CancelledQueuedMonitor_IsRemovedWithoutBlockingFollower()
    {
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        using var cancelled = new CancellationTokenSource();

        var first = coordinator.SendOnceAsync(11, "c11", "first", async () =>
        {
            firstEntered.TrySetResult(true);
            await releaseFirst.Task;
            return true;
        }, null, CancellationToken.None);

        await firstEntered.Task;
        var second = coordinator.SendOnceAsync(12, "c12", "second", () => Task.FromResult(true), null, cancelled.Token);
        var third = coordinator.SendOnceAsync(13, "c13", "third", () => Task.FromResult(true), null, CancellationToken.None);
        cancelled.Cancel();
        releaseFirst.TrySetResult(true);

        await first;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.True(await third);
    }

    [Fact]
    public async Task UncertainIdenticalSend_IsNotPhysicallyClickedTwice()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        var physicalClicks = 0;
        Assert.False(await coordinator.SendOnceAsync(
            21, "conversation-21", "same-message",
            () => { Interlocked.Increment(ref physicalClicks); return Task.FromResult(false); },
            null, CancellationToken.None));
        Assert.False(await coordinator.SendOnceAsync(
            21, "conversation-21", "same-message",
            () => { Interlocked.Increment(ref physicalClicks); return Task.FromResult(true); },
            null, CancellationToken.None));
        Assert.Equal(1, physicalClicks);
        Assert.Equal(OutboundDeliveryPhase.ReconcileRequired, coordinator.Snapshot().Single().Phase);
    }

    [Fact]
    public async Task FailedMonitor_DoesNotDeadlockNextMonitor()
    {
        var coordinator = new OutboundDeliveryCoordinator((_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SendOnceAsync(
            31, "c31", "boom", () => throw new InvalidOperationException("send failed"), null, CancellationToken.None));
        Assert.True(await coordinator.SendOnceAsync(32, "c32", "ok", () => Task.FromResult(true), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("Too many requests")]
    [InlineData("You are making requests too quickly")]
    [InlineData("Please wait a few minutes before trying again")]
    [InlineData("We have temporarily limited access to your conversations")]
    public void RateLimitMarkers_AreDetectedCaseInsensitively(string text)
    {
        Assert.True(GlobalChatGptRateLimitCircuitBreaker.ContainsRateLimitMarker(text.ToUpperInvariant()));
    }

    [Fact]
    public void GlobalRateLimit_BackoffProgressesFiveTenFifteenThirty_ThenClears()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);

        breaker.ObserveVisibleState("Too many requests");
        Assert.Equal(1, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(5), breaker.RetryAtUtc);

        now = now.AddMinutes(5);
        breaker.ObserveVisibleState("Too many requests");
        Assert.Equal(2, breaker.BackoffStep);
        Assert.Equal(now.AddMinutes(10), breaker.RetryAtUtc);

        now = now.AddMinutes(10);
        breaker.ObserveVisibleState("Too many requests");
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
    public void RecoveryRotationAndResponseContinuationShareCanonicalQueueBackedSendMethod()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        Assert.Contains("RotateByMessageCountAsync", source, StringComparison.Ordinal);
        Assert.Contains("TimeoutRecoveryMessage", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptErrorContinuationMessage", source, StringComparison.Ordinal);
        Assert.Contains("var freshResult = await ContinueInFreshChatAfterResponseAsync", source, StringComparison.Ordinal);
        Assert.Contains("sendOutcome = await SendWhenReadyAsync", source, StringComparison.Ordinal);
        var method = source[source.IndexOf("private async Task<SendWhenReadyOutcome> SendWhenReadyAsync", StringComparison.Ordinal)..];
        Assert.Contains("_outboundDelivery.SendOnceAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageVerifiedAsync(tab, message", source[..source.IndexOf("private async Task<SendWhenReadyOutcome> SendWhenReadyAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
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
