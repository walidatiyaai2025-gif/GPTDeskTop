namespace GPTDeskTop.Services;
public sealed record ExternalWaitDashboardItem(string ProjectId, string TaskId, string Dependency, ExternalWaitStatus Status, DateTimeOffset Since, DateTimeOffset? NextCheckAt, string Detail);
