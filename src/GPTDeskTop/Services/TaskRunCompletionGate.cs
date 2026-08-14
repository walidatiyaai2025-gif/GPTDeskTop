namespace GPTDeskTop.Services;
public static class TaskRunCompletionGate
{
    public static bool CanFinalize(bool leaseOwned, bool runRecordPersisted, bool verificationSettled) => leaseOwned && runRecordPersisted && verificationSettled;
}
