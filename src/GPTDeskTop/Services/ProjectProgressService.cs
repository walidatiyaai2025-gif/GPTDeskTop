using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ProjectProgress(int Total, int Completed, int InProgress, int Pending, int Blocked, int Verifying, int AwaitingApproval)
{
    public int Remaining => Math.Max(0, Total - Completed);
    public double PercentComplete => Total == 0 ? 0 : Math.Round(Completed * 100d / Total, 1);
}

public sealed record DashboardCounts(int Total, int Completed, int Active, int Pending, int Blocked, int Verifying, int AwaitingApproval);
public sealed record CurrentWorkCard(string TaskId, string Title, string Status, int? ChatGeneration, DateTimeOffset? StartedAt);

public static class ProjectProgressService
{
    public static ProjectProgress Calculate(ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ProjectProgress(
            state.Tasks.Count,
            state.Tasks.Count(t => t.Status == ProjectTaskStatus.Completed),
            state.Tasks.Count(t => t.Status == ProjectTaskStatus.InProgress),
            state.Tasks.Count(t => t.Status is ProjectTaskStatus.Discovered or ProjectTaskStatus.Ready),
            state.Tasks.Count(t => t.Status == ProjectTaskStatus.Blocked),
            state.Tasks.Count(t => t.Status == ProjectTaskStatus.Verifying),
            state.Tasks.Count(t => t.Status == ProjectTaskStatus.AwaitingApproval));
    }

    public static IReadOnlyList<ProjectTaskState> ByStatus(ProjectState state, ProjectTaskStatus status) => state.Tasks.Where(t => t.Status == status).ToArray();

    public static DashboardCounts Dashboard(ProjectState state)
    {
        var p = Calculate(state);
        return new(p.Total, p.Completed, p.InProgress, p.Pending, p.Blocked, p.Verifying, p.AwaitingApproval);
    }

    public static string ProgressText(ProjectState state)
    {
        var p = Calculate(state);
        return $"{p.Completed}/{p.Total} completed - {p.Remaining} remaining";
    }

    public static IReadOnlyList<ProjectTaskState> Problems(ProjectState state) => state.Tasks.Where(t => t.Status is ProjectTaskStatus.Blocked or ProjectTaskStatus.AwaitingApproval).ToArray();

    public static CurrentWorkCard? CurrentWork(ProjectState state)
    {
        var task = state.Tasks.FirstOrDefault(t => t.Status == ProjectTaskStatus.InProgress);
        return task is null ? null : new(task.TaskId, task.Title, task.Status.ToString(), task.AssignedChatGeneration, task.StartedAt);
    }
}
