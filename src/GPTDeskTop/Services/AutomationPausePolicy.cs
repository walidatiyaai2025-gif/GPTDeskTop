namespace GPTDeskTop.Services;
public static class AutomationPausePolicy
{
    public static bool MustPause(bool humanVerificationRequired, bool awaitingApproval) => humanVerificationRequired || awaitingApproval;
}
