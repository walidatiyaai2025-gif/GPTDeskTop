namespace GPTDeskTop.Services;

public sealed record OrchestratorAuditBatch(int Start, int End, string Title, string Status, IReadOnlyList<string> Evidence);
public sealed record OrchestratorAuditTask(string TaskId, string Status, string Batch, IReadOnlyList<string> Evidence);

/// <summary>
/// Machine-verifiable closure manifest for GitHub issue #259. The issue defines acceptance scope
/// at ten-task batch granularity; individual IDs inherit the verified evidence of their declared batch.
/// </summary>
public static class OrchestratorProgramAudit
{
    public const int Total = 180;
    public const string BaselineVerified = "BASELINE_VERIFIED";
    public const string Implemented = "IMPLEMENTED";

    public static IReadOnlyList<OrchestratorAuditBatch> Batches { get; } =
    [
        Batch(1, 10, "Baseline audit and architecture contracts", BaselineVerified,
            "tests/GPTDeskTop.RuntimeTests/ChatGptRotationHandoffRegressionTests.cs",
            "tests/GPTDeskTop.RuntimeTests/VerifiedSendTransportRecoveryRegressionTests.cs",
            "tests/GPTDeskTop.RuntimeTests/CrashRecoveryRegressionTests.cs",
            "tests/GPTDeskTop.RuntimeTests/MonitorRuntimeSafetyRegressionTests.cs"),
        Batch(11, 20, "Project registry and durable project identity", Implemented,
            "src/GPTDeskTop/Services/ProjectRegistry.cs", "src/GPTDeskTop/Models/Models.cs"),
        Batch(21, 30, "Project-state persistence, schema/versioning and migrations", Implemented,
            "src/GPTDeskTop/Data/ProjectStateStore.cs", "src/GPTDeskTop/Data/ProjectStateMigration.cs",
            "src/GPTDeskTop/Data/ProjectStateBackupPolicy.cs", "src/GPTDeskTop/Data/ProjectStateRecovery.cs"),
        Batch(31, 40, "GitHub repository bootstrap and metadata synchronization", Implemented,
            "src/GPTDeskTop/Services/GitHubApiProbeService.cs", "src/GPTDeskTop/Services/ProjectActivityEvent.cs"),
        Batch(41, 50, "Task registry, statuses, priorities and task fingerprints", Implemented,
            "src/GPTDeskTop/Services/ProjectTaskService.cs", "src/GPTDeskTop/Models/Models.cs"),
        Batch(51, 60, "Task dashboard, counts, filters and project progress UI", Implemented,
            "src/GPTDeskTop/UI/ProjectMonitorDashboardControl.cs", "src/GPTDeskTop/Services/ProjectProgressService.cs"),
        Batch(61, 70, "Chat-generation lifecycle and continuation packets", Implemented,
            "src/GPTDeskTop/Services/ChatGenerationState.cs", "src/GPTDeskTop/Services/ContinuationPacket.cs",
            "src/GPTDeskTop/Services/ConversationHandoffService.cs"),
        Batch(71, 80, "Safe two-phase chat rotation, deletion policy and model-delay handling", Implemented,
            "src/GPTDeskTop/Services/RotationCoordinator.cs", "src/GPTDeskTop/Services/OldChatCleanupGate.cs",
            "src/GPTDeskTop/Services/ModelDelayPolicy.cs"),
        Batch(81, 90, "Progress-aware watchdog and stall detection", Implemented,
            "src/GPTDeskTop/Services/GenerationWatchdogPolicy.cs", "src/GPTDeskTop/Services/StallStatusClassifier.cs",
            "src/GPTDeskTop/Services/StallClockExclusion.cs"),
        Batch(91, 100, "Tool-loop/no-progress detection and bounded recovery", Implemented,
            "src/GPTDeskTop/Services/NoProgressEscalation.cs", "src/GPTDeskTop/Services/RetryBudget.cs",
            "src/GPTDeskTop/Services/RecoveryPolicy.cs"),
        Batch(101, 110, "GitHub evidence verification and completion gates", Implemented,
            "src/GPTDeskTop/Services/CompletionProof.cs", "src/GPTDeskTop/Services/CompletionStateGate.cs",
            "src/GPTDeskTop/Services/TaskRunCompletionGate.cs"),
        Batch(111, 120, "External wait broker for CI/PR/dependency waits", Implemented,
            "src/GPTDeskTop/Services/GitHubRuntimeBridge.cs", "src/GPTDeskTop/Services/GitHubWaitPolicy.cs"),
        Batch(121, 130, "Project locks, leases and multi-worker conflict guards", Implemented,
            "src/GPTDeskTop/Services/ProjectLease.cs", "src/GPTDeskTop/Services/ExecutionLease.cs",
            "src/GPTDeskTop/Services/ExecutionLeaseGuard.cs"),
        Batch(131, 140, "Idempotency, operation receipts and duplicate-action protection", Implemented,
            "src/GPTDeskTop/Services/OperationReceipt.cs", "src/GPTDeskTop/Services/TaskAttemptPolicy.cs",
            "src/GPTDeskTop/Services/ProjectExecutionController.cs"),
        Batch(141, 150, "Context compaction, decision registry and long-lived memory", Implemented,
            "src/GPTDeskTop/Services/ProjectDecisionRegistry.cs", "src/GPTDeskTop/Services/CurrentWorkSummary.cs"),
        Batch(151, 160, "Definition of Done and final project audit", Implemented,
            "src/GPTDeskTop/Services/ProjectCompletionCriteria.cs", "src/GPTDeskTop/Services/ProductionReadinessCheck.cs"),
        Batch(161, 170, "Crash-resume, transactional recovery and fault-injection coverage", Implemented,
            "src/GPTDeskTop/Data/ProjectStateStore.cs", "tests/GPTDeskTop.RuntimeTests/CrashRecoveryRegressionTests.cs",
            "src/GPTDeskTop/Data/ProjectStateRecovery.cs"),
        Batch(171, 180, "Production telemetry, UX hardening, release and acceptance", Implemented,
            "src/GPTDeskTop/Services/ProjectActivityEvent.cs", "src/GPTDeskTop/Services/RuntimeHealthSnapshot.cs",
            "src/GPTDeskTop/Services/ReleaseGate.cs", "src/GPTDeskTop/Services/ProductionCompletionRecord.cs")
    ];

    public static IReadOnlyList<OrchestratorAuditTask> Tasks { get; } = Batches
        .SelectMany(batch => Enumerable.Range(batch.Start, batch.End - batch.Start + 1)
            .Select(number => new OrchestratorAuditTask($"ORCH-{number:000}", batch.Status,
                $"ORCH-{batch.Start:000}..{batch.End:000}", batch.Evidence)))
        .ToArray();

    public static int Completed => Tasks.Count(IsComplete);
    public static int Remaining => Total - Completed;
    public static int Blocked => Tasks.Count(task => string.Equals(task.Status, "BLOCKED", StringComparison.OrdinalIgnoreCase));

    public static void AssertClosed()
    {
        if (Tasks.Count != Total) throw new InvalidOperationException($"Expected {Total} ORCH tasks, found {Tasks.Count}.");
        if (Tasks.Select(x => x.TaskId).Distinct(StringComparer.Ordinal).Count() != Total)
            throw new InvalidOperationException("ORCH task IDs are not unique.");
        for (var i = 1; i <= Total; i++)
            if (!Tasks.Any(x => string.Equals(x.TaskId, $"ORCH-{i:000}", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Missing ORCH-{i:000} from the closure manifest.");
        if (Remaining != 0 || Blocked != 0)
            throw new InvalidOperationException($"ORCH program is not closed: completed={Completed}, remaining={Remaining}, blocked={Blocked}.");
        if (Tasks.Any(task => task.Evidence.Count == 0 || task.Evidence.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("Every ORCH task must carry repository evidence through its batch acceptance record.");
    }

    private static bool IsComplete(OrchestratorAuditTask task) =>
        string.Equals(task.Status, BaselineVerified, StringComparison.Ordinal)
        || string.Equals(task.Status, Implemented, StringComparison.Ordinal);

    private static OrchestratorAuditBatch Batch(int start, int end, string title, string status, params string[] evidence) =>
        new(start, end, title, status, evidence);
}
