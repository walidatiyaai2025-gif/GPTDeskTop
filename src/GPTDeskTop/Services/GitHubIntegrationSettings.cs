namespace GPTDeskTop.Services;

public sealed record GitHubIntegrationSettings(
    string Repository,
    string Branch,
    bool WatchCommits,
    bool WatchPullRequests,
    bool WatchIssues,
    string Token)
{
    public bool AllAccessibleRepositories { get; init; }
    public IReadOnlyList<string> SelectedRepositories { get; init; } = Array.Empty<string>();

    public static GitHubIntegrationSettings Default => new("", "main", true, true, true, "");
}

public sealed record GitHubRepositoryInfo(string FullName, string DefaultBranch, bool Private);
