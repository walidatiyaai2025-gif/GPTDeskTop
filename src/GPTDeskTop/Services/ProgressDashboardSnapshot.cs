namespace GPTDeskTop.Services;
public sealed record ProgressDashboardSnapshot(ProjectProgressSummary Summary, IReadOnlyList<TaskStatusCount> StatusCounts, IReadOnlyList<ExternalWaitDashboardItem> ExternalWaits, DateTimeOffset UpdatedAt);
