using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class DashboardTaskLabel
{
    public static string For(ProjectTaskStatus status) => status switch
    {
        ProjectTaskStatus.Completed => "Completed",
        ProjectTaskStatus.InProgress => "In Progress",
        ProjectTaskStatus.Blocked => "Blocked",
        ProjectTaskStatus.Verifying => "Verifying",
        ProjectTaskStatus.AwaitingApproval => "Awaiting Approval",
        ProjectTaskStatus.Ready => "Ready",
        _ => status.ToString()
    };
}
