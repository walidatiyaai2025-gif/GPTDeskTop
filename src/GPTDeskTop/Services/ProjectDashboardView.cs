namespace GPTDeskTop.Services;
public sealed record ProjectDashboardView(string ProjectId, string ProjectName, ProjectRuntimeStatus Status, int TotalTasks, int CompletedTasks, int RemainingTasks, int BlockedTasks, DateTimeOffset UpdatedAt);
