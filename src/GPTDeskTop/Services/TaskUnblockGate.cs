namespace GPTDeskTop.Services;
public static class TaskUnblockGate
{
    public static bool CanUnblock(bool blockingConditionCleared, bool requiredApprovalPresent, bool humanActionComplete) => blockingConditionCleared && requiredApprovalPresent && humanActionComplete;
}
