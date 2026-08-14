namespace GPTDeskTop.Services;
public static class RuntimeHeartbeatPolicy
{
    public static bool IsStale(RuntimeHeartbeat heartbeat, DateTimeOffset now, TimeSpan threshold) => now - heartbeat.At > threshold;
    public static bool IsHealthy(RuntimeHeartbeat heartbeat, DateTimeOffset now, TimeSpan threshold) => !IsStale(heartbeat, now, threshold) && heartbeat.ChatResponsive && heartbeat.GitHubResponsive;
}
