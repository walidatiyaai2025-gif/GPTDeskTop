namespace GPTDeskTop.Services;
public static class GitHubRuntimeBridge
{
    public static ProjectRuntimeStatus ToRuntime(GitHubWaitDecision decision) => decision switch
    {
        GitHubWaitDecision.KeepWaiting => ProjectRuntimeStatus.WaitingExternal,
        GitHubWaitDecision.ResumeTask => ProjectRuntimeStatus.Running,
        GitHubWaitDecision.BlockTask => ProjectRuntimeStatus.Blocked,
        GitHubWaitDecision.RequestReview => ProjectRuntimeStatus.WaitingForHuman,
        _ => ProjectRuntimeStatus.Blocked
    };
}
