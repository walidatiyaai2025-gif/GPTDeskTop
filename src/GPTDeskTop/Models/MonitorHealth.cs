namespace GPTDeskTop.Models;

/// <summary>
/// Runtime health state for a ChatGPT monitor worker.
/// Persisted state is used by watchdog and recovery flows.
/// </summary>
public sealed class MonitorHealth
{
    public int Id { get; set; }

    public int MonitorId { get; set; }

    public string Status { get; set; } = "Stopped";

    public DateTime LastHeartbeatUtc { get; set; }

    public DateTime? LastRecoveryUtc { get; set; }

    public int RestartCount { get; set; }

    public string? LastError { get; set; }

    public string? LastTabId { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
