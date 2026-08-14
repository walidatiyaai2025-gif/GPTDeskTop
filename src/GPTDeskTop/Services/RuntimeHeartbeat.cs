namespace GPTDeskTop.Services;
public sealed record RuntimeHeartbeat(string ProjectId, int ChatGeneration, DateTimeOffset At, bool ChatResponsive, bool GitHubResponsive);
