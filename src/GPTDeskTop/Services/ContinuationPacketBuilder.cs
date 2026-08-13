using System.Text;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class ContinuationPacketBuilder
{
    public static string Build(ProjectState state, int recentCompletedLimit = 10)
    {
        ArgumentNullException.ThrowIfNull(state);
        var progress = ProjectProgressService.Calculate(state);
        var recent = state.Tasks.Where(t => t.Status == ProjectTaskStatus.Completed).OrderByDescending(t => t.CompletedAt).Take(Math.Clamp(recentCompletedLimit, 0, 50)).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("Continue the existing project. Do not redo completed work.");
        sb.AppendLine($"Repository: {state.RepoUrl}");
        sb.AppendLine($"Project: {state.ProjectName}");
        sb.AppendLine($"Main goal: {state.MainGoal}");
        sb.AppendLine($"Phase: {state.CurrentPhase}");
        sb.AppendLine($"Tasks: {progress.Completed}/{progress.Total} completed; {progress.Remaining} remaining; {progress.Blocked} blocked.");
        sb.AppendLine($"Branch: {state.CurrentBranch}; PR: {state.CurrentPR}; Last commit: {state.LastCommit}");
        sb.AppendLine($"Chat generation: {state.ChatGeneration}");
        if (state.Rules.Count > 0) { sb.AppendLine("Rules:"); foreach (var rule in state.Rules) sb.AppendLine($"- {rule}"); }
        if (state.KnownErrors.Count > 0) { sb.AppendLine("Known errors:"); foreach (var error in state.KnownErrors) sb.AppendLine($"- {error}"); }
        if (recent.Length > 0) { sb.AppendLine("Recent completed tasks:"); foreach (var task in recent) sb.AppendLine($"- {task.TaskId}: {task.Title}"); }
        sb.AppendLine($"Next action: {state.NextAction}");
        sb.AppendLine("Inspect current GitHub state before changing code and report only verifiable progress.");
        return sb.ToString().Trim();
    }
}
