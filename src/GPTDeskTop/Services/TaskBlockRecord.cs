namespace GPTDeskTop.Services;
public sealed record TaskBlockRecord(string TaskId, BlockedTaskReason Reason, string Detail, DateTimeOffset BlockedAt, bool RequiresHuman);
