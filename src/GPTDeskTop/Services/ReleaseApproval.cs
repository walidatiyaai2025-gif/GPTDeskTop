namespace GPTDeskTop.Services;
public sealed record ReleaseApproval(string Version, bool ReadinessPassed, bool SmokePassed, bool ArtifactVerified, DateTimeOffset ApprovedAt)
{
    public bool Approved => ReadinessPassed && SmokePassed && ArtifactVerified;
}
