namespace GPTDeskTop.Runtime;

/// <summary>Immutable presentation contract. UI consumers must not make runtime decisions from it.</summary>
public sealed record RuntimeUiSnapshot(
    string GlobalSendState,
    int QueuedCount,
    long? CurrentMonitorId,
    string? CurrentMonitorName,
    ChatGptRuntimeState ChatGptState,
    AutonomousTaskPhase? CurrentTaskState,
    bool RateLimitActive,
    DateTimeOffset? NextProbeUtc,
    TimeSpan RateLimitRemaining);
