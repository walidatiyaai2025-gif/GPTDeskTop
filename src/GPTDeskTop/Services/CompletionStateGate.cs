namespace GPTDeskTop.Services;
public static class CompletionStateGate
{
    public static bool Ready(bool implementationDone, bool verificationDone, bool evidencePresent) => implementationDone && verificationDone && evidencePresent;
}
