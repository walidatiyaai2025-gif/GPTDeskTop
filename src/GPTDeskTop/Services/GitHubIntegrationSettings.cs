namespace GPTDeskTop.Services;

public sealed record GitHubRepositoryCredential(
    string Repository,
    string Branch,
    string Token,
    bool UseSharedToken = false);

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
    public IReadOnlyList<GitHubRepositoryCredential> RepositoryCredentials { get; init; } = Array.Empty<GitHubRepositoryCredential>();

    public string ResolveToken(string repository)
    {
        var credential = RepositoryCredentials.FirstOrDefault(x =>
            string.Equals(x.Repository, repository, StringComparison.OrdinalIgnoreCase));
        return credential is not null && !credential.UseSharedToken && !string.IsNullOrWhiteSpace(credential.Token)
            ? credential.Token
            : Token;
    }

    public string ResolveBranch(string repository)
    {
        var credential = RepositoryCredentials.FirstOrDefault(x =>
            string.Equals(x.Repository, repository, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(credential?.Branch) ? credential.Branch : Branch;
    }

    public bool HasCredentialFor(string repository) => !string.IsNullOrWhiteSpace(ResolveToken(repository));

    public static GitHubIntegrationSettings Default => new("", "main", true, true, true, "");
}

public sealed record GitHubRepositoryInfo(string FullName, string DefaultBranch, bool Private);
