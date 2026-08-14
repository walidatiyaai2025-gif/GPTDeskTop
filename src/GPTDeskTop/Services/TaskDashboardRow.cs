using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public sealed record TaskDashboardRow(string Id, string Title, string Status, string Priority, int? ChatGeneration, int? Issue, int? PullRequest, string BlockedReason)
{
    public static TaskDashboardRow From(ProjectTaskState task) => new(task.TaskId, task.Title, DashboardTaskLabel.For(task.Status), task.Priority, task.AssignedChatGeneration, task.GitHubIssue, task.GitHubPR, task.BlockedReason);
}
