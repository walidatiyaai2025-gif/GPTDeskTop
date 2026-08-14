namespace GPTDeskTop.Services;

public sealed record TaskDashboardEvidence(
    string TaskId,
    string? CommitSha,
    int? IssueNumber,
    int? PullRequestNumber,
    DateTimeOffset VerifiedAt)
{
    public bool HasRepositoryEvidence => !string.IsNullOrWhiteSpace(CommitSha) || IssueNumber.HasValue || PullRequestNumber.HasValue;
}
