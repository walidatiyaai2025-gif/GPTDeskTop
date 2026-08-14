using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed class GitHubIntegrationStore
{
    private readonly LocalDatabase _database;
    public GitHubIntegrationStore(LocalDatabase database) => _database = database;

    public async Task<GitHubIntegrationSettings> LoadAsync()
    {
        var token = string.Empty;
        var protectedToken = await _database.GetSettingAsync("GitHub.Token.Protected");
        if (!string.IsNullOrWhiteSpace(protectedToken))
        {
            try { token = GitHubTokenProtector.Unprotect(protectedToken); }
            catch { token = string.Empty; }
        }

        var selected = (await _database.GetSettingAsync("GitHub.SelectedRepositories") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GitHubIntegrationSettings(
            await _database.GetSettingAsync("GitHub.Repository") ?? string.Empty,
            await _database.GetSettingAsync("GitHub.Branch") ?? "main",
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchCommits"), "0", StringComparison.Ordinal),
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchPullRequests"), "0", StringComparison.Ordinal),
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchIssues"), "0", StringComparison.Ordinal),
            token)
        {
            AllAccessibleRepositories = string.Equals(await _database.GetSettingAsync("GitHub.AllAccessibleRepositories"), "1", StringComparison.Ordinal),
            SelectedRepositories = selected
        };
    }

    public Task SaveAsync(GitHubIntegrationSettings settings)
        => _database.SetSettingsAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GitHub.Repository"] = settings.Repository.Trim(),
            ["GitHub.Branch"] = settings.Branch.Trim(),
            ["GitHub.AllAccessibleRepositories"] = settings.AllAccessibleRepositories ? "1" : "0",
            ["GitHub.SelectedRepositories"] = string.Join(';', settings.SelectedRepositories.Distinct(StringComparer.OrdinalIgnoreCase)),
            ["GitHub.WatchCommits"] = settings.WatchCommits ? "1" : "0",
            ["GitHub.WatchPullRequests"] = settings.WatchPullRequests ? "1" : "0",
            ["GitHub.WatchIssues"] = settings.WatchIssues ? "1" : "0",
            ["GitHub.Token.Protected"] = GitHubTokenProtector.Protect(settings.Token)
        });

    public Task DisconnectAsync()
        => _database.SetSettingsAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GitHub.Repository"] = string.Empty,
            ["GitHub.Branch"] = "main",
            ["GitHub.AllAccessibleRepositories"] = "0",
            ["GitHub.SelectedRepositories"] = string.Empty,
            ["GitHub.WatchCommits"] = "1",
            ["GitHub.WatchPullRequests"] = "1",
            ["GitHub.WatchIssues"] = "1",
            ["GitHub.Token.Protected"] = string.Empty
        });
}
