namespace GPTDeskTop.Models;

public sealed class ChromeTab
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string WebSocketDebuggerUrl { get; set; } = string.Empty;
}

public sealed record ChatPageState(
    int AssistantCount,
    string LastAssistantText,
    bool IsGenerating,
    string ErrorText,
    string GlobalRateLimitText = "");

public sealed class SavedMonitor
{
    private string _runtimeStatus = "Stopped";

    public long Id { get; set; }
    public string TabId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AutoReply { get; set; } = "كمل";
    public int ReplyDelaySeconds { get; set; } = 3;
    public int TimerSeconds { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool ConversationRotationEnabled { get; set; } = true;
    public string NewChatStartMessage { get; set; } = "كمل";
    public int NewChatDelaySeconds { get; set; } = 30;
    public int RotationCooldownSeconds { get; set; } = 60;
    public int MaxConversationRotations { get; set; } = 0;
    public int RotationCount { get; set; }
    public bool ModelRoutingEnabled { get; set; } = false;
    public string PreferredModel { get; set; } = "Auto";
    public string FallbackModel { get; set; } = "Auto";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string RuntimeStatus
    {
        get => string.Equals(_runtimeStatus, "Running", StringComparison.OrdinalIgnoreCase) ? "🟢 Running" : "🔴 Stopped";
        set => _runtimeStatus = value?.Contains("Running", StringComparison.OrdinalIgnoreCase) == true ? "Running" : "Stopped";
    }
}

public sealed class MessageLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public long? MonitorId { get; set; }
    public string TabId { get; set; } = string.Empty;
    public string TabTitle { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public enum ProjectTaskStatus
{
    Discovered,
    Ready,
    InProgress,
    Verifying,
    Completed,
    Blocked,
    Superseded,
    AwaitingApproval
}

public sealed class ProjectState
{
    public int StateVersion { get; set; } = 1;
    public string ProjectId { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string MainGoal { get; set; } = string.Empty;
    public List<string> Rules { get; set; } = [];
    public string CurrentPhase { get; set; } = string.Empty;
    public string Status { get; set; } = "IDLE";
    public List<ProjectTaskState> Tasks { get; set; } = [];
    public string CurrentBranch { get; set; } = "main";
    public string CurrentPR { get; set; } = string.Empty;
    public string LastCommit { get; set; } = string.Empty;
    public List<string> KnownErrors { get; set; } = [];
    public List<string> ImportantDecisions { get; set; } = [];
    public string NextAction { get; set; } = string.Empty;
    public int ChatGeneration { get; set; } = 1;
    public string CurrentChatId { get; set; } = string.Empty;
    public long CurrentMonitorId { get; set; }
    public int HealthScore { get; set; } = 100;
    public int RetryCount { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectTaskState
{
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Discovered;
    public string Priority { get; set; } = "Normal";
    public int? AssignedChatGeneration { get; set; }
    public int? GitHubIssue { get; set; }
    public int? GitHubPR { get; set; }
    public string Branch { get; set; } = string.Empty;
    public string LastCommit { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
    public List<string> VerificationEvidence { get; set; } = [];
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
