namespace GPTDeskTop.Services;
public sealed record RuntimeWiringState(string ProjectId, bool ChatBound, bool GitHubBound, bool SchedulerBound, bool DashboardBound)
{
    public bool Ready => ChatBound && GitHubBound && SchedulerBound && DashboardBound;
}
