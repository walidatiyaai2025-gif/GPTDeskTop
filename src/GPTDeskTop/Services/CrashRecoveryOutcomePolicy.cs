namespace GPTDeskTop.Services;

public enum CrashRecoveryOutcome
{
    Success,
    SendFailed,
    InvalidConversationIdentity,
    Cancelled
}

public static class CrashRecoveryOutcomePolicy
{
    public static bool ShouldStartMonitor(CrashRecoveryOutcome outcome, bool enabled) =>
        enabled && outcome == CrashRecoveryOutcome.Success;

    public static bool ShouldClearPending(IReadOnlyCollection<CrashRecoveryOutcome> outcomes) =>
        outcomes.Count > 0 && outcomes.All(x => x == CrashRecoveryOutcome.Success);
}
