using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed record NewProjectMonitorDraft(
    string Repository,
    string Branch,
    string ProjectInstruction,
    string MonitorReply);

public sealed record NewProjectRepositoryOption(
    string Repository,
    string SuggestedBranch,
    bool HasSavedToken);

public sealed record NewProjectGitHubPreflightResult(
    bool Success,
    string Repository,
    string Branch,
    string Message,
    bool RequiresCredentialUi);

public sealed class NewProjectMonitorWizardService
{
    private readonly GitHubIntegrationStore _store;
    private readonly GitHubApiProbeService _probe;

    public NewProjectMonitorWizardService(LocalDatabase database, GitHubApiProbeService? probe = null)
    {
        _store = new GitHubIntegrationStore(database ?? throw new ArgumentNullException(nameof(database)));
        _probe = probe ?? new GitHubApiProbeService();
    }

    public async Task<IReadOnlyList<NewProjectRepositoryOption>> LoadRepositoryOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.LoadAsync();
        var repositories = settings.AllAccessibleRepositories
            ? settings.SelectedRepositories
            : string.IsNullOrWhiteSpace(settings.Repository)
                ? Array.Empty<string>()
                : new[] { settings.Repository };

        var result = new List<NewProjectRepositoryOption>();
        foreach (var repository in repositories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = settings.ResolveToken(repository);
            var configuredBranch = settings.ResolveBranch(repository);
            var suggestedBranch = string.IsNullOrWhiteSpace(configuredBranch) ? "main" : configuredBranch;

            if (!string.IsNullOrWhiteSpace(token))
            {
                var probe = await _probe.TestAsync(new GitHubIntegrationSettings(
                    repository,
                    suggestedBranch,
                    settings.WatchCommits,
                    settings.WatchPullRequests,
                    settings.WatchIssues,
                    token), cancellationToken);

                if (!string.IsNullOrWhiteSpace(probe.DefaultBranch))
                    suggestedBranch = probe.DefaultBranch!;
                else if (probe.Branches.Contains("main", StringComparer.Ordinal))
                    suggestedBranch = "main";
            }

            result.Add(new NewProjectRepositoryOption(repository, suggestedBranch, !string.IsNullOrWhiteSpace(token)));
        }

        return result.OrderBy(x => x.Repository, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<NewProjectGitHubPreflightResult> ValidateAsync(NewProjectMonitorDraft draft, CancellationToken cancellationToken = default)
    {
        var repositoryError = GitHubIntegrationValidator.ValidateRepository(draft.Repository);
        if (repositoryError is not null)
            return new(false, draft.Repository, draft.Branch, repositoryError, false);

        var settings = await _store.LoadAsync();
        var token = settings.ResolveToken(draft.Repository);
        if (string.IsNullOrWhiteSpace(token))
            return new(false, draft.Repository, draft.Branch, $"No saved GitHub token is available for {draft.Repository}.", true);

        var branch = string.IsNullOrWhiteSpace(draft.Branch) ? settings.ResolveBranch(draft.Repository) : draft.Branch.Trim();
        if (string.IsNullOrWhiteSpace(branch)) branch = "main";

        var result = await _probe.TestAsync(new GitHubIntegrationSettings(
            draft.Repository.Trim(),
            branch,
            settings.WatchCommits,
            settings.WatchPullRequests,
            settings.WatchIssues,
            token), cancellationToken);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.DefaultBranch))
        {
            branch = result.DefaultBranch!;
            result = await _probe.TestAsync(new GitHubIntegrationSettings(
                draft.Repository.Trim(),
                branch,
                settings.WatchCommits,
                settings.WatchPullRequests,
                settings.WatchIssues,
                token), cancellationToken);
        }

        if (!result.Success && result.Branches.Contains("main", StringComparer.Ordinal) && !string.Equals(branch, "main", StringComparison.Ordinal))
        {
            branch = "main";
            result = await _probe.TestAsync(new GitHubIntegrationSettings(
                draft.Repository.Trim(),
                branch,
                settings.WatchCommits,
                settings.WatchPullRequests,
                settings.WatchIssues,
                token), cancellationToken);
        }

        var requiresCredentialUi = !result.Success &&
            (result.Message.Contains("401", StringComparison.OrdinalIgnoreCase)
             || result.Message.Contains("403", StringComparison.OrdinalIgnoreCase)
             || result.Message.Contains("token", StringComparison.OrdinalIgnoreCase)
             || result.Message.Contains("credential", StringComparison.OrdinalIgnoreCase));

        return new(result.Success, draft.Repository.Trim(), branch, result.Message, requiresCredentialUi);
    }
}
