using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ProjectRegistry
{
    private readonly ProjectStateStore _store;

    public ProjectRegistry(ProjectStateStore store) => _store = store;

    public async Task<ProjectState> GetOrCreateAsync(
        string repoUrl,
        string? mainGoal = null,
        CancellationToken cancellationToken = default)
    {
        var identity = ParseRepository(repoUrl);
        var existing = await _store.LoadAsync(identity.ProjectId, cancellationToken);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(mainGoal)) existing.MainGoal = mainGoal.Trim();
            existing.RepoUrl = identity.CanonicalUrl;
            await _store.SaveAsync(existing, cancellationToken);
            return existing;
        }

        var state = new ProjectState
        {
            ProjectId = identity.ProjectId,
            RepoUrl = identity.CanonicalUrl,
            ProjectName = identity.Repository,
            MainGoal = mainGoal?.Trim() ?? string.Empty,
            CurrentPhase = "bootstrap",
            Status = "BOOTSTRAPPING",
            CurrentBranch = "main",
            NextAction = "Inspect repository and determine the highest-priority actionable work.",
            ChatGeneration = 1
        };
        await _store.SaveAsync(state, cancellationToken);
        return state;
    }

    public Task<ProjectState?> GetAsync(string projectId, CancellationToken cancellationToken = default) =>
        _store.LoadAsync(projectId, cancellationToken);

    public IReadOnlyList<string> ListProjectIds() => _store.ListProjectIds();

    public static RepositoryIdentity ParseRepository(string repoUrl)
    {
        if (!Uri.TryCreate(repoUrl?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A valid github.com repository URL is required.", nameof(repoUrl));

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) throw new ArgumentException("GitHub URL must contain owner and repository.", nameof(repoUrl));
        var owner = segments[0];
        var repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("GitHub URL must contain owner and repository.", nameof(repoUrl));
        return new RepositoryIdentity($"{owner}/{repository}".ToLowerInvariant(), owner, repository, $"https://github.com/{owner}/{repository}");
    }
}

public sealed record RepositoryIdentity(string ProjectId, string Owner, string Repository, string CanonicalUrl);
