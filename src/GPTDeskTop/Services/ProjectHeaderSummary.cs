using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class ProjectHeaderSummary
{
    public static string Build(ProjectState state)
    {
        var p = ProjectProgressService.Calculate(state);
        return $"{state.ProjectName} | {state.CurrentPhase} | {p.Completed}/{p.Total} completed | health {state.HealthScore}% | chat #{state.ChatGeneration}";
    }
}
