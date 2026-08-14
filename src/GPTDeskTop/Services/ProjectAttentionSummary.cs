using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public sealed record ProjectAttentionSummary(int Blocked, int AwaitingApproval, int Verifying, int KnownErrors)
{
    public bool NeedsAttention => Blocked + AwaitingApproval + KnownErrors > 0;
    public static ProjectAttentionSummary From(ProjectState state) => new(
        state.Tasks.Count(t => t.Status == ProjectTaskStatus.Blocked),
        state.Tasks.Count(t => t.Status == ProjectTaskStatus.AwaitingApproval),
        state.Tasks.Count(t => t.Status == ProjectTaskStatus.Verifying),
        state.KnownErrors.Count);
}
