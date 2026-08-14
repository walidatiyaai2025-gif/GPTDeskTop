namespace GPTDeskTop.Services;
public enum RotationRollbackAction { PreserveOldChat, RetryNewChat, RequireManualReview }
public static class RotationRollbackPolicy
{
    public static RotationRollbackAction Decide(bool oldChatAvailable, int newChatAttempts)
    {
        if (oldChatAvailable) return RotationRollbackAction.PreserveOldChat;
        return newChatAttempts < 2 ? RotationRollbackAction.RetryNewChat : RotationRollbackAction.RequireManualReview;
    }
}
