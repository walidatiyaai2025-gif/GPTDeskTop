namespace GPTDeskTop.Services;
public static class PrProceedPolicy
{
    public static bool Ready(PullRequestRuntimeState state, GitHubCheckSummary checks, bool reviewOk) => state is PullRequestRuntimeState.Open or PullRequestRuntimeState.Mergeable && checks.AllSucceeded && reviewOk;
}
