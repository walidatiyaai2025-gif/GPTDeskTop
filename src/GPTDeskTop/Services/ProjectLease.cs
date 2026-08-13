namespace GPTDeskTop.Services;
public sealed record ProjectLease(string ProjectId, string OwnerMonitorId, DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public static ProjectLease Acquire(string projectId, string ownerMonitorId, TimeSpan duration, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        return new ProjectLease(projectId, ownerMonitorId, at, at.Add(duration));
    }
}
