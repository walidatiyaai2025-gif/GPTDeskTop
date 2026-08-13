using GPTDeskTop.Models;
namespace GPTDeskTop.Data;
public static class ProjectStateValidator
{
    public static bool IsValid(ProjectState state) =>
        state is not null &&
        !string.IsNullOrWhiteSpace(state.ProjectId) &&
        !string.IsNullOrWhiteSpace(state.RepoUrl) &&
        state.ChatGeneration >= 1 &&
        state.HealthScore is >= 0 and <= 100 &&
        state.RetryCount >= 0;
}
