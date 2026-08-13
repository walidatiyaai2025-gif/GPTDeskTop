using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public sealed record CurrentWorkSummary(string? TaskId, string? Title, ProjectTaskStatus? Status, int ChatGeneration);
public static class CurrentWorkSummaryBuilder
{
    public static CurrentWorkSummary Build(ProjectState state)
    {
        var task = state.Tasks.FirstOrDefault(t => t.Status == ProjectTaskStatus.InProgress) ?? state.Tasks.FirstOrDefault(t => t.Status == ProjectTaskStatus.Verifying);
        return new(task?.TaskId, task?.Title, task?.Status, state.ChatGeneration);
    }
}
