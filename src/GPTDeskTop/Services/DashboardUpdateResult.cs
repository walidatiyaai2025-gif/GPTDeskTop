namespace GPTDeskTop.Services;
public sealed record DashboardUpdateResult(string ProjectId, bool Updated, DateTimeOffset At, string Detail);
