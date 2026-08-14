namespace GPTDeskTop.Services;
public sealed record ExecutionLease(string ProjectId, string TaskId, int ChatGeneration, DateTimeOffset AcquiredAt, DateTimeOffset ExpiresAt)
{
    public bool ActiveAt(DateTimeOffset now) => now < ExpiresAt;
}
