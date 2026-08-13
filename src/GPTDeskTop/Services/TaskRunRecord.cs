namespace GPTDeskTop.Services;
public sealed record TaskRunRecord(string ProjectId, string TaskId, int ChatGeneration, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Outcome, string Detail);
