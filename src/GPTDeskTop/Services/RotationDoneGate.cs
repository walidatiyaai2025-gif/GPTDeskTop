namespace GPTDeskTop.Services;
public static class RotationDoneGate
{
    public static bool Ready(bool continuationVerified, bool destinationHealthy, bool sourceCleanupDone) => continuationVerified && destinationHealthy && sourceCleanupDone;
}
