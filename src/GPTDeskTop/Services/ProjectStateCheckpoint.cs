using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ProjectStateCheckpoint(
    string CheckpointId,
    string ProjectId,
    string Status,
    string NextAction,
    string CurrentBranch,
    string CurrentPR,
    string LastCommit,
    int ChatGeneration,
    DateTimeOffset CreatedAt)
{
    public static ProjectStateCheckpoint Capture(ProjectState state, string? checkpointId = null) =>
        new(
            checkpointId ?? Guid.NewGuid().ToString("N"),
            state.ProjectId,
            state.Status,
            state.NextAction,
            state.CurrentBranch,
            state.CurrentPR,
            state.LastCommit,
            state.ChatGeneration,
            DateTimeOffset.UtcNow);
}
