using System.Collections.Concurrent;
using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Runtime;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class FinalRuntimeJourneyTests
{
    private const string LimitedText = "Too many requests. Please wait a few minutes before trying again.";

    [Fact]
    [Trait("Category", "IntegratedRuntimeE2E")]
    public async Task ThreeMonitorJourneySurvivesRateLimitRestartUncertainSendRolloverAndCompletionGate()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "runtime.db"));
            await database.InitializeAsync();
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));
            var breaker = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await breaker.InitializeAsync(database);

            var taskPath = Path.Combine(root, "autonomous-task.json");
            var autonomousTask = new AutonomousTaskController(taskPath, "task-e2e-001", "chat-a");
            autonomousTask.Transition(AutonomousTaskPhase.TaskRunning, "startup-restored-monitor-state");

            var coordinator = new OutboundDeliveryCoordinator(
                delayAsync: null,
                interSendGap: null,
                rateLimit: breaker);
            var physicalTimes = new ConcurrentDictionary<long, DateTimeOffset>();
            var cPhysicalSends = 0;
            using var cCancellation = new CancellationTokenSource();

            var sendA = coordinator.SendOnceAsync(
                101,
                "chat-a",
                "A",
                () =>
                {
                    physicalTimes[101] = DateTimeOffset.UtcNow;
                    return Task.FromResult(true);
                },
                null,
                CancellationToken.None);

            var sendB = coordinator.SendOnceAsync(
                102,
                "chat-b",
                "B",
                () =>
                {
                    physicalTimes[102] = DateTimeOffset.UtcNow;
                    breaker.ObserveVisibleState(LimitedText);
                    return Task.FromResult(true);
                },
                null,
                CancellationToken.None);

            var sendCBeforeRestart = coordinator.SendOnceAsync(
                103,
                "chat-c",
                "C",
                () =>
                {
                    Interlocked.Increment(ref cPhysicalSends);
                    physicalTimes[103] = DateTimeOffset.UtcNow;
                    return Task.FromResult(true);
                },
                null,
                cCancellation.Token);

            Assert.True(await sendA);
            Assert.True(await sendB);
            var rateLimitDetected = breaker.IsActive;
            Assert.True(rateLimitDetected);
            var rateLimitPersisted = string.Equals(
                "1",
                await database.GetSettingAsync(GlobalChatGptRateLimitCircuitBreaker.IsActiveKey),
                StringComparison.Ordinal);
            Assert.True(rateLimitPersisted);
            var minimumGapMilliseconds = (physicalTimes[102] - physicalTimes[101]).TotalMilliseconds;
            Assert.True(minimumGapMilliseconds >= 5000);

            await Task.Delay(150);
            var queueSerialized = !sendCBeforeRestart.IsCompleted && cPhysicalSends == 0;
            Assert.True(queueSerialized);

            autonomousTask.Transition(AutonomousTaskPhase.WaitingForChatGpt, "global-rate-limit-pause");
            cCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await sendCBeforeRestart;
            });

            var restoredTask = new AutonomousTaskController(taskPath, "ignored-after-restart", "ignored-after-restart");
            Assert.Equal("task-e2e-001", restoredTask.Snapshot.TaskId);
            Assert.Equal(AutonomousTaskPhase.WaitingForChatGpt, restoredTask.Snapshot.Phase);

            var restoredBreaker = new GlobalChatGptRateLimitCircuitBreaker(clock.Read);
            await restoredBreaker.InitializeAsync(database);
            var restartRestored = restoredBreaker.IsActive && restoredBreaker.RetryAtUtc == breaker.RetryAtUtc;
            Assert.True(restartRestored);

            clock.UtcNow = restoredBreaker.RetryAtUtc!.Value;
            var probeClears = 0;
            restoredBreaker.StatusChanged += status =>
            {
                if (status.EventName == "RateLimitCleared") Interlocked.Increment(ref probeClears);
            };
            restoredBreaker.ObserveVisibleState(null);
            restoredBreaker.ObserveVisibleState(null);
            Assert.Equal(1, probeClears);
            Assert.False(restoredBreaker.IsActive);

            var resumedCoordinator = new OutboundDeliveryCoordinator(
                delayAsync: (_, _) => Task.CompletedTask,
                interSendGap: TimeSpan.Zero,
                rateLimit: restoredBreaker);
            var resumedAfterClear = await resumedCoordinator.SendOnceAsync(
                103,
                "chat-c",
                "C",
                () =>
                {
                    Interlocked.Increment(ref cPhysicalSends);
                    return Task.FromResult(true);
                },
                null,
                CancellationToken.None);
            Assert.True(resumedAfterClear);
            Assert.Equal(1, cPhysicalSends);

            var timeoutPhysicalSends = 0;
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await resumedCoordinator.SendOnceAsync(
                    104,
                    "chat-timeout",
                    "recover-me",
                    () =>
                    {
                        Interlocked.Increment(ref timeoutPhysicalSends);
                        return Task.FromException<bool>(new TimeoutException("simulated delivery timeout"));
                    },
                    null,
                    CancellationToken.None);
            });
            Assert.Equal(1, timeoutPhysicalSends);
            Assert.Equal(
                OutboundDeliveryPhase.ReconcileRequired,
                resumedCoordinator.Snapshot().Single(snapshot => snapshot.MonitorId == 104).Phase);

            var duplicatePhysicalSends = 0;
            var duplicateAccepted = await resumedCoordinator.SendOnceAsync(
                104,
                "chat-timeout",
                "recover-me",
                () =>
                {
                    Interlocked.Increment(ref duplicatePhysicalSends);
                    return Task.FromResult(true);
                },
                null,
                CancellationToken.None);
            Assert.False(duplicateAccepted);
            Assert.Equal(0, duplicatePhysicalSends);
            resumedCoordinator.MarkCompleted(104);
            Assert.Equal(
                OutboundDeliveryPhase.Completed,
                resumedCoordinator.Snapshot().Single(snapshot => snapshot.MonitorId == 104).Phase);

            var taskIdBeforeRollover = restoredTask.Snapshot.TaskId;
            restoredTask.Rollover("chat-rollover-2");
            var taskIdAfterRollover = restoredTask.Snapshot.TaskId;
            Assert.Equal(taskIdBeforeRollover, taskIdAfterRollover);
            Assert.Equal("chat-rollover-2", restoredTask.Snapshot.ConversationKey);
            Assert.Equal(AutonomousTaskPhase.ConversationRollover, restoredTask.Snapshot.Phase);

            var prematureDoneRejected = !restoredTask.TryComplete(
                true,
                new(true, true, false, false, true, false, true));
            Assert.True(prematureDoneRejected);
            Assert.Equal(AutonomousTaskPhase.VerifyingCompletion, restoredTask.Snapshot.Phase);
            var completedAfterEvidence = restoredTask.TryComplete(
                false,
                new(true, true, true, true, true, true, true));
            Assert.True(completedAfterEvidence);
            Assert.Equal(AutonomousTaskPhase.Completed, restoredTask.Snapshot.Phase);

            await CrashRecoveryStateService.MarkCleanShutdownAsync(database);
            Assert.Equal("1", await database.GetSettingAsync("LastShutdownClean"));

            WriteReceipt("final-runtime-journey-receipt.json", new
            {
                sourceSha = SourceSha(),
                queueSerialized,
                minimumGapMilliseconds,
                rateLimitDetected,
                rateLimitPersisted,
                restartRestored,
                probeCount = probeClears,
                resumedAfterClear,
                uncertainSendDuplicateCount = duplicatePhysicalSends,
                taskIdBeforeRollover,
                taskIdAfterRollover,
                prematureDoneRejected,
                completedAfterEvidence,
                passed = true
            });
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void WriteReceipt(string fileName, object receipt)
    {
        var directory = Environment.GetEnvironmentVariable("GPTDESKTOP_RUNTIME_CLOSURE_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string SourceSha()
        => Environment.GetEnvironmentVariable("GPTDESKTOP_RUNTIME_CLOSURE_SOURCE_SHA") ?? "LOCAL";

    private sealed class MutableClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public DateTimeOffset Read() => UtcNow;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-runtime-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
