namespace GPTDeskTop.Services;

public static class GitHubIntegrationValidator
{
    public static string? ValidateRepository(string? repository)
    {
        var value = repository?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "Repository is required (owner/repo).";
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return "Repository must use owner/repo format.";
        if (parts.Any(p => p.Length == 0 || p.Any(ch => char.IsWhiteSpace(ch)))) return "Repository contains invalid whitespace.";
        return null;
    }

    public static string? ValidateBranch(string? branch)
        => string.IsNullOrWhiteSpace(branch) ? "Branch is required." : null;

    public static (string Owner, string Repo) SplitRepository(string repository)
    {
        var parts = repository.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts[1]);
    }
}
