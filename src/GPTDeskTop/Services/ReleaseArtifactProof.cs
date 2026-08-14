namespace GPTDeskTop.Services;
public sealed record ReleaseArtifactProof(string ArtifactName, string CommitSha, string Sha256, long SizeBytes, DateTimeOffset VerifiedAt);
