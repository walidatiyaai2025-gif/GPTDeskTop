namespace GPTDeskTop.Services;
public sealed record QueuedProjectTask(string ProjectId, string TaskId, int Priority, DateTimeOffset EnqueuedAt);
