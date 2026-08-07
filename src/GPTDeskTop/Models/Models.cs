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
    string ErrorText);

public sealed class SavedMonitor
{
    public long Id { get; set; }
    public string TabId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AutoReply { get; set; } = "كمل";
    public int ReplyDelaySeconds { get; set; } = 3;
    public int TimerSeconds { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string RuntimeStatus { get; set; } = "Stopped";
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
