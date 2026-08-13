namespace GPTDeskTop.Services;
public static class ProjectQueuePolicy
{
    public static IEnumerable<ProjectQueueItem> Order(IEnumerable<ProjectQueueItem> items) => items.OrderByDescending(x => x.Priority).ThenBy(x => x.EnqueuedAt).ThenBy(x => x.TaskId, StringComparer.Ordinal);
}
