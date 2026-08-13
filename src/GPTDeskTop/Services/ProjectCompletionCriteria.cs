namespace GPTDeskTop.Services;
public sealed class ProjectCompletionCriteria
{
    public bool BuildMustPass { get; init; } = true;
    public bool TestsMustPass { get; init; } = true;
    public int MaxCriticalOpenIssues { get; init; } = 0;
    public bool RequiredPullRequestsMustBeMerged { get; init; } = true;
    public bool ReleaseArtifactRequired { get; init; }
    public bool DocumentationRequired { get; init; }
}
