namespace GPTDeskTop.Services;
public sealed record RuntimeStateSnapshot(string ProjectId, ProjectRuntimeStatus Status, string CurrentTaskId, int ChatGeneration, DateTimeOffset UpdatedAt, string Detail);
