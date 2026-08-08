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
    public string? LastError { get; set; }
    public long Revision { get; set; }
}
