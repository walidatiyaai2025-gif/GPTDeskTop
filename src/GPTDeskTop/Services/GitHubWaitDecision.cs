namespace GPTDeskTop.Services;
public enum GitHubWaitDecision { KeepWaiting, ResumeTask, BlockTask, RequestReview }
public static class GitHubWaitDecisionPolicy
{
    public static GitHubWaitDecision Decide(GitHubCheckState state) => state switch
    {
        GitHubCheckState.Queued or GitHubCheckState.InProgress => GitHubWaitDecision.KeepWaiting,
        GitHubCheckState.Success or GitHubCheckState.Skipped => GitHubWaitDecision.ResumeTask,
        GitHubCheckState.Failure or GitHubCheckState.TimedOut => GitHubWaitDecision.BlockTask,
        _ => GitHubWaitDecision.RequestReview
    };
}
