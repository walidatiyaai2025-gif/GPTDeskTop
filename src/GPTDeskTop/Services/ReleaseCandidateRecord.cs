namespace GPTDeskTop.Services;
public sealed record ReleaseCandidateRecord(string Version, string CommitSha, string ArtifactName, DateTimeOffset CreatedAt, string State, string Detail);
