namespace GPTDeskTop.Services;
public enum GitHubCheckState { Queued, InProgress, Success, Failure, Cancelled, Skipped, TimedOut, Unknown }
public static class GitHubCheckStateExtensions
{
    public static bool IsTerminal(this GitHubCheckState state) => state is GitHubCheckState.Success or GitHubCheckState.Failure or GitHubCheckState.Cancelled or GitHubCheckState.Skipped or GitHubCheckState.TimedOut;
}
