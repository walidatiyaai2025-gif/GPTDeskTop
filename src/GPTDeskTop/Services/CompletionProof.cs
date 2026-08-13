namespace GPTDeskTop.Services;
public sealed record CompletionProof(string TaskId, string? CommitSha, int? PullRequest, bool TestsPassed, string Summary)
{
    public bool Verifiable => !string.IsNullOrWhiteSpace(CommitSha) || PullRequest.HasValue || TestsPassed;
}
