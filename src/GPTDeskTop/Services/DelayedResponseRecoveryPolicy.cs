namespace GPTDeskTop.Services;

public enum DelayedResponseRecoveryAction
{
    Wait,
    RefreshTab,
    ReopenTab,
    RequireHuman
}

public static class DelayedResponseRecoveryPolicy
{
    public static DelayedResponseRecoveryAction Decide(bool humanVerificationVisible, bool responseProgressObserved, int recoveryAttempts)
    {
        if (humanVerificationVisible) return DelayedResponseRecoveryAction.RequireHuman;
        if (responseProgressObserved) return DelayedResponseRecoveryAction.Wait;
        if (recoveryAttempts <= 0) return DelayedResponseRecoveryAction.RefreshTab;
        if (recoveryAttempts == 1) return DelayedResponseRecoveryAction.ReopenTab;
        return DelayedResponseRecoveryAction.RequireHuman;
    }
}
