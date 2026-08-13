namespace GPTDeskTop.Services;

public sealed class ModelDelayPolicy
{
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan ExtendedTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxRecoveryAttempts { get; init; } = 1;
    public int MaxDelayOccurrencesPerTask { get; init; } = 2;
}
