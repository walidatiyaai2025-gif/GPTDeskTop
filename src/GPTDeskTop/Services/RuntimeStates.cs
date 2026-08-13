namespace GPTDeskTop.Services;
public enum RuntimeState
{
    Idle, Bootstrapping, Active, Generating, WaitingExternal, SuspectedStall, Stalled, ToolLoopDetected, Verifying, Recovering, RotatingChat, Blocked, AwaitingApproval, ProjectComplete
}
