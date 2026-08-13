namespace GPTDeskTop.Services;
public enum DelayedResponseAction { Wait, RetryOnce, SuggestFallback, RotateChat, Stop }
public static class DelayedResponseRecovery
{
    public static DelayedResponseAction Decide(bool timedOut, int attempts, bool fallbackAllowed, bool chatHealthy)
    {
        if (!timedOut) return DelayedResponseAction.Wait;
        if (attempts == 0) return DelayedResponseAction.RetryOnce;
        if (fallbackAllowed) return DelayedResponseAction.SuggestFallback;
        if (!chatHealthy) return DelayedResponseAction.RotateChat;
        return DelayedResponseAction.Stop;
    }
}
