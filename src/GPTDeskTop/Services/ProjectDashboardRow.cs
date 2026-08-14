using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public sealed record ProjectDashboardRow(string Name, string Phase, string Status, int Completed, int Total, int Remaining, int Health, int ChatGeneration)
{
    public static ProjectDashboardRow From(ProjectState state)
    {
        var p = ProjectProgressService.Calculate(state);
        return new(state.ProjectName, state.CurrentPhase, state.Status, p.Completed, p.Total, p.Remaining, state.HealthScore, state.ChatGeneration);
    }
}
