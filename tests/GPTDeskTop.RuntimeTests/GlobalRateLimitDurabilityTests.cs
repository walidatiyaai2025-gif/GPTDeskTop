using GPTDeskTop.Data;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalRateLimitDurabilityTests
{
    private const string LimitedText = "Too many requests. Please wait a few minutes before trying again.";

    [Fact]
    public async Task RestartBeforeRetryRestoresGlobalFenceAndBlocksPhysicalSend()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "runtime.db"));
            await database.InitializeAsync();
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));

            var first = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await first.InitializeAsync(database);
            first.ObserveVisibleState(LimitedText);

            Assert.True(first.IsActive);
            Assert.Equal(1, first.BackoffStep);
            Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(5), first.RetryAtUtc);
            Assert.Equal("1", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.IsActiveKey));
            Assert.Equal("0", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.BackoffIndexKey));
            Assert.Equal("too_many_requests", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.LastCategoryKey));
            Assert.Equal("RateLimitDetected|RetryScheduled", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.LastTransitionKey));

            var restored = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await restored.InitializeAsync(database);
            Assert.True(restored.IsActive);
            Assert.Equal(first.DetectedAtUtc, restored.DetectedAtUtc);
            Assert.Equal(first.RetryAtUtc, restored.RetryAtUtc);

            var physicalSends = 0;
            var coordinator = new OutboundDeliveryCoordinator(
                delayAsync: (_, _) => Task.CompletedTask,
                interSendGap: TimeSpan.Zero,
                rateLimit: restored);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.SendOnceAsync(
                1,
                "chat-1",
                "continue",
                () => { Interlocked.Increment(ref physicalSends); return Task.FromResult(true); },
                null,
                cancellation.Token));
            Assert.Equal(0, physicalSends);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task BackoffStagesSurviveRestartAtFiveTenFifteenAndThirtyMinutes()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "runtime.db"));
            await database.InitializeAsync();
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));
            var breaker = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await breaker.InitializeAsync(database);
            breaker.ObserveVisibleState(LimitedText);

            var expectedMinutes = new[] { 5, 10, 15, 30, 30 };
            for (var index = 0; index < expectedMinutes.Length; index++)
            {
                Assert.True(breaker.IsActive);
                Assert.Equal(Math.Min(index + 1, 4), breaker.BackoffStep);
                Assert.Equal(TimeSpan.FromMinutes(expectedMinutes[index]), breaker.RetryAtUtc!.Value - clock.UtcNow);

                var restarted = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
                await restarted.InitializeAsync(database);
                Assert.True(restarted.IsActive);
                Assert.Equal(breaker.BackoffStep, restarted.BackoffStep);
                Assert.Equal(breaker.RetryAtUtc, restarted.RetryAtUtc);
                Assert.Equal(breaker.DetectedAtUtc, restarted.DetectedAtUtc);
                breaker = restarted;

                if (index == expectedMinutes.Length - 1)
                    break;

                clock.UtcNow = breaker.RetryAtUtc!.Value;
                breaker.ObserveVisibleState(LimitedText);
            }

            Assert.Equal("3", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.BackoffIndexKey));
            Assert.Equal("RateLimitStillActive|RetryScheduled", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.LastTransitionKey));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExpiredDeadlineAllowsExactlyOneVisibleProbeButNeverAuthorizesSendByItself()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "runtime.db"));
            await database.InitializeAsync();
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));
            var first = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await first.InitializeAsync(database);
            first.ObserveVisibleState(LimitedText);
            clock.UtcNow = first.RetryAtUtc!.Value;

            var restored = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await restored.InitializeAsync(database);
            Assert.True(restored.IsProbeEligible);

            var physicalSends = 0;
            var blockedCoordinator = new OutboundDeliveryCoordinator(
                delayAsync: (_, _) => Task.CompletedTask,
                interSendGap: TimeSpan.Zero,
                rateLimit: restored);
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedCoordinator.SendOnceAsync(
                    2,
                    "chat-2",
                    "continue",
                    () => { Interlocked.Increment(ref physicalSends); return Task.FromResult(true); },
                    null,
                    cancellation.Token));
            }
            Assert.Equal(0, physicalSends);

            var clearEvents = 0;
            restored.StatusChanged += status =>
            {
                if (status.EventName == "RateLimitCleared") Interlocked.Increment(ref clearEvents);
            };
            restored.ObserveVisibleState(null);
            restored.ObserveVisibleState(null);

            Assert.False(restored.IsActive);
            Assert.Equal(1, clearEvents);
            Assert.Equal("0", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.IsActiveKey));
            Assert.Equal("RateLimitCleared", await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.LastTransitionKey));

            var allowedCoordinator = new OutboundDeliveryCoordinator(
                delayAsync: (_, _) => Task.CompletedTask,
                interSendGap: TimeSpan.Zero,
                rateLimit: restored);
            Assert.True(await allowedCoordinator.SendOnceAsync(
                2,
                "chat-2",
                "continue-after-safe-probe",
                () => { Interlocked.Increment(ref physicalSends); return Task.FromResult(true); },
                null,
                CancellationToken.None));
            Assert.Equal(1, physicalSends);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void StartupRecoveryWiresDurableBreakerBeforeRecoveryAndMonitorResume()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryStateService.cs");
        var initialize = source.IndexOf("GlobalChatGptRateLimitCircuitBreaker.Shared", StringComparison.Ordinal);
        var lastShutdownRead = source.IndexOf("GetSettingAsync(\"LastShutdownClean\"", StringComparison.Ordinal);
        Assert.True(initialize >= 0);
        Assert.True(lastShutdownRead > initialize);
        Assert.Contains(".InitializeAsync(database, cancellationToken)", source, StringComparison.Ordinal);
    }

    private sealed class MutableClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public DateTimeOffset Read() => UtcNow;
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-rate-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
