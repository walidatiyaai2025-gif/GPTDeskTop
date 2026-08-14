using System.Net.Http.Headers;
using System.Text.Json;

namespace GPTDeskTop.Services;

public sealed class GitHubApiProbeService
{
    public async Task<IReadOnlyList<GitHubRepositoryInfo>> ListRepositoriesAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("GitHub token is required.");
        using var client = CreateClient(token);
        var repos = new List<GitHubRepositoryInfo>();
        for (var page = 1; page <= 20; page++)
        {
            using var doc = await GetJsonAsync(client, $"user/repos?per_page=100&page={page}&affiliation=owner,collaborator,organization_member&sort=full_name", cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) break;
            var count = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                count++;
                var fullName = item.TryGetProperty("full_name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(fullName)) continue;
                var defaultBranch = item.TryGetProperty("default_branch", out var d) ? d.GetString() ?? "main" : "main";
                var isPrivate = item.TryGetProperty("private", out var p) && p.GetBoolean();
                repos.Add(new GitHubRepositoryInfo(fullName, defaultBranch, isPrivate));
            }
            if (count < 100) break;
        }
        return repos.DistinctBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<GitHubConnectionResult> TestAsync(GitHubIntegrationSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Token)) return new(false, "GitHub token is required.", null, null, false, Array.Empty<string>());
        if (settings.AllAccessibleRepositories)
        {
            try
            {
                using var client = CreateClient(settings.Token);
                using var user = await GetJsonAsync(client, "user", cancellationToken);
                var login = user.RootElement.TryGetProperty("login", out var loginNode) ? loginNode.GetString() : null;
                var repos = await ListRepositoriesAsync(settings.Token, cancellationToken);
                if (repos.Count == 0) return new(false, "Connected, but the token exposes no repositories.", login, null, false, Array.Empty<string>());
                return new(true, $"Connected as {login ?? "GitHub user"}. {repos.Count} accessible repositories loaded.", login, null, false, Array.Empty<string>());
            }
            catch (Exception ex) { return new(false, $"GitHub connection failed: {ex.Message}", null, null, false, Array.Empty<string>()); }
        }

        var repositoryError = GitHubIntegrationValidator.ValidateRepository(settings.Repository);
        if (repositoryError is not null) return new(false, repositoryError, null, null, false, Array.Empty<string>());
        var branchError = GitHubIntegrationValidator.ValidateBranch(settings.Branch);
        if (branchError is not null) return new(false, branchError, null, null, false, Array.Empty<string>());
        var (owner, repo) = GitHubIntegrationValidator.SplitRepository(settings.Repository);
        using var singleClient = CreateClient(settings.Token);
        try
        {
            using var user = await GetJsonAsync(singleClient, "user", cancellationToken);
            var login = user.RootElement.TryGetProperty("login", out var loginNode) ? loginNode.GetString() : null;
            using var repoDoc = await GetJsonAsync(singleClient, $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}", cancellationToken);
            var defaultBranch = repoDoc.RootElement.TryGetProperty("default_branch", out var defaultNode) ? defaultNode.GetString() : null;
            var isPrivate = repoDoc.RootElement.TryGetProperty("private", out var privateNode) && privateNode.GetBoolean();
            using var branchesDoc = await GetJsonAsync(singleClient, $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches?per_page=100", cancellationToken);
            var branches = branchesDoc.RootElement.ValueKind == JsonValueKind.Array ? branchesDoc.RootElement.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
            if (!branches.Contains(settings.Branch, StringComparer.Ordinal)) return new(false, $"Connected, but branch '{settings.Branch}' was not found.", login, defaultBranch, isPrivate, branches);
            return new(true, $"Connected as {login ?? "GitHub user"}. Repository and branch are accessible.", login, defaultBranch, isPrivate, branches);
        }
        catch (HttpRequestException ex) { return new(false, $"GitHub connection failed: {ex.Message}", null, null, false, Array.Empty<string>()); }
        catch (TaskCanceledException) { return new(false, "GitHub connection timed out.", null, null, false, Array.Empty<string>()); }
        catch (Exception ex) { return new(false, $"GitHub validation failed: {ex.Message}", null, null, false, Array.Empty<string>()); }
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GPTDeskTop/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractGitHubMessage(body)}");
        return JsonDocument.Parse(body);
    }

    private static string ExtractGitHubMessage(string body)
    {
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("message", out var node)) return node.GetString() ?? "GitHub API error"; } catch { }
        return "GitHub API error";
    }
}
