namespace GPTDeskTop.Services;
public sealed record RecoveryAttempt(string ProjectId, int AttemptNumber, RecoveryAction Action, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool Successful, string Detail);
