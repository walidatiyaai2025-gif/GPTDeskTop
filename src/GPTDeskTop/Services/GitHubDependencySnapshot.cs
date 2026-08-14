namespace GPTDeskTop.Services;
public sealed record GitHubDependencySnapshot(string Repository, string Identifier, GitHubCheckState State, DateTimeOffset ObservedAt, string Detail);
