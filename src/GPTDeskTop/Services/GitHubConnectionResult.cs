namespace GPTDeskTop.Services;

public sealed record GitHubConnectionResult(
    bool Success,
    string Message,
    string? AuthenticatedUser,
    string? DefaultBranch,
    bool PrivateRepository,
    IReadOnlyList<string> Branches);
