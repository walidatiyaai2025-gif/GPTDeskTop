namespace GPTDeskTop.Services;
public sealed record WatchdogHeartbeat(DateTimeOffset At, DateTimeOffset LastProgressAt, bool IsPaused)
{
    public TimeSpan EffectiveIdle => IsPaused ? TimeSpan.Zero : At - LastProgressAt;
}
