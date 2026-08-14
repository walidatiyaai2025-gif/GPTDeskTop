namespace GPTDeskTop.Services;
public static class ChatContinuationVerification
{
    public static bool CanDeleteOldChat(bool checkpointSaved, bool newChatCreated, bool continuationAccepted, bool composerHealthy) => checkpointSaved && newChatCreated && continuationAccepted && composerHealthy;
}
