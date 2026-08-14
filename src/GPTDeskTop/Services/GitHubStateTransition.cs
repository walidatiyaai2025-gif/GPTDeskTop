namespace GPTDeskTop.Services;
public sealed record GitHubStateTransition(GitHubCheckState Previous, GitHubCheckState Current, DateTimeOffset At)
{
    public bool HasChanged => Previous != Current;
}
