namespace GPTDeskTop.Services;
public static class OldChatCleanupGate
{
    public static bool CanDelete(bool checkpointDurable, bool newChatCreated, bool continuationAccepted, bool newChatHealthy) => checkpointDurable && newChatCreated && continuationAccepted && newChatHealthy;
}
