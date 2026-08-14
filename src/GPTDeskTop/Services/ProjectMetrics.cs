using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class ProjectMetrics
{
    public static int Health(ProjectState state)
    {
        var penalty = Math.Min(30, state.Tasks.Count(t => t.Status == ProjectTaskStatus.Blocked) * 5)
                    + Math.Min(20, state.KnownErrors.Count * 4)
                    + Math.Min(20, state.RetryCount * 5);
        return Math.Clamp(100 - penalty, 0, 100);
    }
}
