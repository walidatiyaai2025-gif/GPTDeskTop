namespace GPTDeskTop.Services;

public sealed record WatchdogProgressObservation(bool UiChanged, bool GitHubChanged, bool ToolActivityObserved, DateTimeOffset ObservedAt)
{
    public bool HasProgress => UiChanged || GitHubChanged || ToolActivityObserved;
}
