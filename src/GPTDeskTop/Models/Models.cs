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
    bool IsGenerating);

public sealed class MessageLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
