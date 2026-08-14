namespace GPTDeskTop.Services;
public sealed record ExternalDependencyWait(string Type, string Id, DateTimeOffset StartedAt, DateTimeOffset Deadline)
{
    public bool TimedOut(DateTimeOffset now) => now >= Deadline;
}
