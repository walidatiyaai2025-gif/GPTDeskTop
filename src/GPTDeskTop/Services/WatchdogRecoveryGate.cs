namespace GPTDeskTop.Services;

public static class WatchdogRecoveryGate
{
    public static bool ShouldRecover(WatchdogProgressObservation observation, bool humanVerificationVisible, bool responsePending)
    {
        if (humanVerificationVisible) return false;
        if (!responsePending) return false;
        return !observation.HasProgress;
    }
}
