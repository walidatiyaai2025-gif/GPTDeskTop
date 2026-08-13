namespace GPTDeskTop.Services;
public sealed record ProjectActivityEvent(DateTimeOffset Timestamp, string ProjectId, string Type, string Message, string? TaskId = null, string? ChatId = null)
{
    public static ProjectActivityEvent Create(string projectId, string type, string message, string? taskId = null, string? chatId = null) =>
        new(DateTimeOffset.UtcNow, projectId, type, message, taskId, chatId);
}
