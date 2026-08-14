using GPTDeskTop.Models;

namespace GPTDeskTop.Data;

public static class ProjectStateMigration
{
    public const int CurrentVersion = 1;

    public static ProjectState Upgrade(ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.StateVersion > CurrentVersion)
            throw new InvalidOperationException($"Unsupported project-state version {state.StateVersion}.");
        if (state.StateVersion <= 0) state.StateVersion = 1;
        return state;
    }
}
