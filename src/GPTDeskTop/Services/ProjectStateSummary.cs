using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public sealed record ProjectStateSummary(string ProjectId, string ProjectName, string Status, string CurrentPhase, int TotalTasks, int CompletedTasks, int RemainingTasks, int BlockedTasks, int HealthScore, string NextAction);
public static class ProjectStateSummaryBuilder
{
    public static ProjectStateSummary Build(ProjectState state)
    {
        var progress = ProjectProgressService.Calculate(state);
        return new(state.ProjectId, state.ProjectName, state.Status, state.CurrentPhase, progress.Total, progress.Completed, progress.Remaining, progress.Blocked, state.HealthScore, state.NextAction);
    }
}
