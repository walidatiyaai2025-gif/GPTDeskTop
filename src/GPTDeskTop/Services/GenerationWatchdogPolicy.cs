namespace GPTDeskTop.Services;
public sealed class GenerationWatchdogPolicy
{
    public TimeSpan InactivityWarning { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SuspectedStall { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan HardStall { get; init; } = TimeSpan.FromMinutes(20);
    public int MaxStopRecoveryAttempts { get; init; } = 1;
    public int MaxSameChatRecoveries { get; init; } = 1;
    public int ToolLoopCycleThreshold { get; init; } = 3;

    public TimeSpan EffectiveIdle(DateTimeOffset lastProgressAt, DateTimeOffset now, TimeSpan pausedDuration)
        => TimeSpan.FromTicks(Math.Max(0, (now - lastProgressAt - pausedDuration).Ticks));
}
