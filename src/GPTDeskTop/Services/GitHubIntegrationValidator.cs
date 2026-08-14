namespace GPTDeskTop.Services;

public static class GitHubIntegrationValidator
{
    public static string? ValidateRepository(string? repository)
    {
        var value = repository?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "Repository is required (owner/repo).";
        if (value.Any(char.IsWhiteSpace)) return "Repository contains invalid whitespace.";
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts.Any(p => p.Length == 0)) return "Repository must use owner/repo format.";
        return null;
    }

    public static string? ValidateBranch(string? branch)
        => string.IsNullOrWhiteSpace(branch) ? "Branch is required." : null;

    public static (string Owner, string Repo) SplitRepository(string repository)
    {
        var parts = repository.Trim().Split('/', 2);
        return (parts[0], parts[1]);
    }
}
