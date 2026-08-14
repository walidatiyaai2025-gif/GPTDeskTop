namespace GPTDeskTop.Services;
public static class ExecutionLeaseGuard
{
    public static bool CanAcquire(ExecutionLease? current, DateTimeOffset now) => current is null || !current.ActiveAt(now);
    public static bool Owns(ExecutionLease lease, string projectId, string taskId, int chatGeneration) =>
        lease.ProjectId == projectId && lease.TaskId == taskId && lease.ChatGeneration == chatGeneration;
}
