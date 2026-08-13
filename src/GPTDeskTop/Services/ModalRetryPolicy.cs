namespace GPTDeskTop.Services;

public sealed class ModalRetryPolicy
{
    public int MaxAttempts { get; init; } = 2;
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(2);
    public bool CanRetry(int attempts) => attempts < MaxAttempts;
}
