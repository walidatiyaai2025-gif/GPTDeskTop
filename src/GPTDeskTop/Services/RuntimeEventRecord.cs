namespace GPTDeskTop.Services;
public sealed record RuntimeEventRecord(string ProjectId, RuntimeEventKind Kind, string? TaskId, int ChatGeneration, DateTimeOffset At, string Detail);
