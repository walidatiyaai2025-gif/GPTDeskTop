namespace GPTDeskTop.Services;
public sealed class RecoveryAttemptTracker
{
    public int StopAttempts { get; private set; }
    public int SameChatRecoveries { get; private set; }
    public void RecordStop() => StopAttempts++;
    public void RecordSameChatRecovery() => SameChatRecoveries++;
    public bool ShouldRotate(GenerationWatchdogPolicy policy) => StopAttempts > policy.MaxStopRecoveryAttempts || SameChatRecoveries > policy.MaxSameChatRecoveries;
}
