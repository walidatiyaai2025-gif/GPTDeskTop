namespace GPTDeskTop.Services;
public sealed record TaskAttempt(string TaskId, int AttemptNumber, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool Successful, string Result);
