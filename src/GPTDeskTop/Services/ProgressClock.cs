namespace GPTDeskTop.Services;
public sealed class ProgressClock
{
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastProgressAt { get; private set; } = DateTimeOffset.UtcNow;
    public void Start(DateTimeOffset now) => StartedAt = LastProgressAt = now;
    public void RecordProgress(DateTimeOffset now) { if (now > LastProgressAt) LastProgressAt = now; }
    public TimeSpan IdleFor(DateTimeOffset now) => now - LastProgressAt;
}
