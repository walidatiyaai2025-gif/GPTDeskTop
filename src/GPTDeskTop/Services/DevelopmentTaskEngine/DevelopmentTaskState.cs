namespace GPTDeskTop.Services.DevelopmentTaskEngine;

public enum DevelopmentTaskEngineStatus
{
    Stopped,
    Working,
    Cooling,
    Paused,
    Completed,
    Faulted
}

public sealed class DevelopmentTaskState : EventArgs
{
    public string PlanId { get; set; } = "default-development-plan";
    public string PlanTitle { get; set; } = "Development Plan";
    public int CurrentMessageIndex { get; set; }
    public int CompletedMessages { get; set; }
    public int TotalMessages { get; set; }
    public DevelopmentTaskEngineStatus Status { get; set; } = DevelopmentTaskEngineStatus.Stopped;
    public DateTimeOffset? WorkWindowStartedAt { get; set; }
    public DateTimeOffset? CoolingStartedAt { get; set; }
    public DateTimeOffset? LastCheckpointAt { get; set; }
    public string? LastMonitorId { get; set; }
    public string? LastTabId { get; set; }
    public int LastDeliveredMessageIndex { get; set; } = -1;
    public string? LastDeliveredMessageFingerprint { get; set; }
    public Dictionary<string, DevelopmentTaskDeliveryReceipt> DeliveryReceipts { get; set; } = new(StringComparer.Ordinal);
    public string? LastError { get; set; }
    public long Revision { get; set; }
}

public sealed class DevelopmentTaskDeliveryReceipt
{
    public string MonitorId { get; set; } = string.Empty;
    public string TabId { get; set; } = string.Empty;
    public int MessageIndex { get; set; } = -1;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset DeliveredAt { get; set; }
    public long Revision { get; set; }
}
