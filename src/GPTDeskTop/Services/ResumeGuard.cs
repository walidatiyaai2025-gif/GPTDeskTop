namespace GPTDeskTop.Services;
public static class ResumeGuard
{
    public static bool NeedsRetry(bool confirmed, bool knownFailed) => knownFailed || !confirmed;
}
