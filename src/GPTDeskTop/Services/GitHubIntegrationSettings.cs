namespace GPTDeskTop.Services;

public sealed record GitHubIntegrationSettings(
    string Repository,
    string Branch,
    bool WatchCommits,
    bool WatchPullRequests,
    bool WatchIssues,
    string Token)
{
    public static GitHubIntegrationSettings Default => new("", "main", true, true, true, "");
}
