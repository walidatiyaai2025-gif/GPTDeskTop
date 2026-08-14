namespace GPTDeskTop.Services;
public sealed class ContinuationBackoff
{
    public int MaxAttempts { get; init; } = 2;
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(5);
    public bool Allowed(int attempts) => attempts < MaxAttempts;
}
