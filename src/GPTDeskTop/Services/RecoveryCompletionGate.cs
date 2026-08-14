namespace GPTDeskTop.Services;
public static class RecoveryCompletionGate
{
    public static bool CanComplete(bool runtimeHealthy, bool checkpointConsistent, bool taskLeaseValid) => runtimeHealthy && checkpointConsistent && taskLeaseValid;
}
