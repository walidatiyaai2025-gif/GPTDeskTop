using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ProjectProgress(int Total, int Completed, int InProgress, int Pending, int Blocked, int Verifying, int AwaitingApproval)
{
    public int Remaining => Math.Max(0, Total - Completed);
    public double PercentComplete => Total == 0 ? 0 : Math.Round(Completed * 100d / Total, 1);
}

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
}
