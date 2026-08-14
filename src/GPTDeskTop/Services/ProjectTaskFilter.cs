using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class ProjectTaskFilter
{
    public static IReadOnlyList<ProjectTaskState> Active(ProjectState state) => state.Tasks.Where(t => t.Status is ProjectTaskStatus.InProgress or ProjectTaskStatus.Verifying).ToArray();
    public static IReadOnlyList<ProjectTaskState> Blocked(ProjectState state) => state.Tasks.Where(t => t.Status == ProjectTaskStatus.Blocked).ToArray();
}
