namespace GPTDeskTop.Services;
public static class TaskAttemptPolicy
{
    public static bool CanRetry(int attemptNumber, int maximumAttempts = 3) => attemptNumber < maximumAttempts;
}
