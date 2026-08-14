namespace GPTDeskTop.Services;
public sealed record ProductionCompletionRecord(string Version, string CommitSha, string ArtifactName, DateTimeOffset CompletedAt, string Detail);
