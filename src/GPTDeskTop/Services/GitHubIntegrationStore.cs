using System.Text.Json;
using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed class GitHubIntegrationStore
{
    private sealed record StoredRepositoryCredential(string Repository, string Branch, string ProtectedToken, bool UseSharedToken);

    private readonly LocalDatabase _database;
    public GitHubIntegrationStore(LocalDatabase database) => _database = database;

    public async Task<GitHubIntegrationSettings> LoadAsync()
    {
        var token = UnprotectOrEmpty(await _database.GetSettingAsync("GitHub.Token.Protected"));
        var selected = (await _database.GetSettingAsync("GitHub.SelectedRepositories") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var credentials = LoadRepositoryCredentials(await _database.GetSettingAsync("GitHub.RepositoryCredentials.Protected"));

        return new GitHubIntegrationSettings(
            await _database.GetSettingAsync("GitHub.Repository") ?? string.Empty,
            await _database.GetSettingAsync("GitHub.Branch") ?? "main",
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchCommits"), "0", StringComparison.Ordinal),
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchPullRequests"), "0", StringComparison.Ordinal),
            !string.Equals(await _database.GetSettingAsync("GitHub.WatchIssues"), "0", StringComparison.Ordinal),
            token)
        {
            AllAccessibleRepositories = string.Equals(await _database.GetSettingAsync("GitHub.AllAccessibleRepositories"), "1", StringComparison.Ordinal),
            SelectedRepositories = selected,
            RepositoryCredentials = credentials
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
            ["GitHub.Token.Protected"] = ProtectOrEmpty(settings.Token),
            ["GitHub.RepositoryCredentials.Protected"] = SerializeRepositoryCredentials(settings.RepositoryCredentials)
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
            ["GitHub.Token.Protected"] = string.Empty,
            ["GitHub.RepositoryCredentials.Protected"] = string.Empty
        });

    private static string SerializeRepositoryCredentials(IEnumerable<GitHubRepositoryCredential> credentials)
    {
        var stored = credentials
            .Where(x => !string.IsNullOrWhiteSpace(x.Repository))
            .GroupBy(x => x.Repository.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .Select(x => new StoredRepositoryCredential(
                x.Repository.Trim(),
                string.IsNullOrWhiteSpace(x.Branch) ? "main" : x.Branch.Trim(),
                x.UseSharedToken ? string.Empty : ProtectOrEmpty(x.Token),
                x.UseSharedToken))
            .ToArray();
        return stored.Length == 0 ? string.Empty : JsonSerializer.Serialize(stored);
    }

    private static IReadOnlyList<GitHubRepositoryCredential> LoadRepositoryCredentials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<GitHubRepositoryCredential>();
        try
        {
            return (JsonSerializer.Deserialize<StoredRepositoryCredential[]>(json) ?? Array.Empty<StoredRepositoryCredential>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Repository))
                .Select(x => new GitHubRepositoryCredential(
                    x.Repository.Trim(),
                    string.IsNullOrWhiteSpace(x.Branch) ? "main" : x.Branch.Trim(),
                    x.UseSharedToken ? string.Empty : UnprotectOrEmpty(x.ProtectedToken),
                    x.UseSharedToken))
                .ToArray();
        }
        catch
        {
            return Array.Empty<GitHubRepositoryCredential>();
        }
    }

    private static string ProtectOrEmpty(string? token)
        => string.IsNullOrWhiteSpace(token) ? string.Empty : GitHubTokenProtector.Protect(token.Trim());

    private static string UnprotectOrEmpty(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return string.Empty;
        try { return GitHubTokenProtector.Unprotect(protectedToken); }
        catch { return string.Empty; }
    }
}
