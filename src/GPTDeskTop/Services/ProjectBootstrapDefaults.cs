using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class ProjectBootstrapDefaults
{
    public static void Apply(ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.StateVersion = Math.Max(1, state.StateVersion);
        state.CurrentPhase = string.IsNullOrWhiteSpace(state.CurrentPhase) ? "bootstrap" : state.CurrentPhase;
        state.Status = string.IsNullOrWhiteSpace(state.Status) ? "BOOTSTRAPPING" : state.Status;
        state.CurrentBranch = string.IsNullOrWhiteSpace(state.CurrentBranch) ? "main" : state.CurrentBranch;
        state.ChatGeneration = Math.Max(1, state.ChatGeneration);
        state.HealthScore = Math.Clamp(state.HealthScore <= 0 ? 100 : state.HealthScore, 0, 100);
        state.NextAction = string.IsNullOrWhiteSpace(state.NextAction)
            ? "Inspect repository and determine the highest-priority actionable work."
            : state.NextAction;
    }
}
