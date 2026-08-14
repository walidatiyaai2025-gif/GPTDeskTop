namespace GPTDeskTop.Services;
public static class ProjectQueuePolicy
{
    public static IEnumerable<QueuedProjectTask> Order(IEnumerable<QueuedProjectTask> items) => items.OrderByDescending(x => x.Priority).ThenBy(x => x.EnqueuedAt).ThenBy(x => x.TaskId, StringComparer.Ordinal);
}
