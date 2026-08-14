using System.Net.Http.Headers;
using System.Text.Json;

namespace GPTDeskTop.Services;

public sealed class GitHubApiProbeService
{
    public async Task<GitHubConnectionResult> TestAsync(GitHubIntegrationSettings settings, CancellationToken cancellationToken = default)
    {
        var repositoryError = GitHubIntegrationValidator.ValidateRepository(settings.Repository);
        if (repositoryError is not null) return new(false, repositoryError, null, null, false, Array.Empty<string>());
        var branchError = GitHubIntegrationValidator.ValidateBranch(settings.Branch);
        if (branchError is not null) return new(false, branchError, null, null, false, Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(settings.Token)) return new(false, "GitHub token is required.", null, null, false, Array.Empty<string>());

        var (owner, repo) = GitHubIntegrationValidator.SplitRepository(settings.Repository);
        using var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GPTDeskTop/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token.Trim());
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            var user = await GetJsonAsync(client, "user", cancellationToken);
            var login = user.RootElement.TryGetProperty("login", out var loginNode) ? loginNode.GetString() : null;

            var repoDoc = await GetJsonAsync(client, $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}", cancellationToken);
            var defaultBranch = repoDoc.RootElement.TryGetProperty("default_branch", out var defaultNode) ? defaultNode.GetString() : null;
            var isPrivate = repoDoc.RootElement.TryGetProperty("private", out var privateNode) && privateNode.GetBoolean();

            var branchesDoc = await GetJsonAsync(client, $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches?per_page=100", cancellationToken);
            var branches = branchesDoc.RootElement.ValueKind == JsonValueKind.Array
                ? branchesDoc.RootElement.EnumerateArray()
                    .Select(x => x.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            if (!branches.Contains(settings.Branch, StringComparer.Ordinal))
                return new(false, $"Connected, but branch '{settings.Branch}' was not found.", login, defaultBranch, isPrivate, branches);

            return new(true, $"Connected as {login ?? "GitHub user"}. Repository and branch are accessible.", login, defaultBranch, isPrivate, branches);
        }
        catch (HttpRequestException ex)
        {
            return new(false, $"GitHub connection failed: {ex.Message}", null, null, false, Array.Empty<string>());
        }
        catch (TaskCanceledException)
        {
            return new(false, "GitHub connection timed out.", null, null, false, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new(false, $"GitHub validation failed: {ex.Message}", null, null, false, Array.Empty<string>());
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractGitHubMessage(body)}");
        return JsonDocument.Parse(body);
    }

    private static string ExtractGitHubMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var node)) return node.GetString() ?? "GitHub API error";
        }
        catch { }
        return "GitHub API error";
    }
}
