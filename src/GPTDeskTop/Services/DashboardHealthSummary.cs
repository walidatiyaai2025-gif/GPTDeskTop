namespace GPTDeskTop.Services;
public sealed record DashboardHealthSummary(int RunningProjects, int WaitingProjects, int BlockedProjects, int CompletedProjects, DashboardFreshness Freshness);
