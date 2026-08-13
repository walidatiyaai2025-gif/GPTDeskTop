namespace GPTDeskTop.Services;
public sealed class ProjectExecutionPolicy
{
    public int MaxChatGenerationsPerTask { get; init; } = 5;
    public int MaxRecoveryAttempts { get; init; } = 2;
    public int MaxRepeatedPromptCount { get; init; } = 2;
    public int MaxNoRepoChangeCycles { get; init; } = 3;
    public TimeSpan MaxExternalWait { get; init; } = TimeSpan.FromHours(2);
}
