namespace GPTDeskTop.Services;
public sealed record ProjectActivityEvent(DateTimeOffset Timestamp, string ProjectId, string Type, string Message, string? TaskId = null, string? ChatId = null)
{
    public static ProjectActivityEvent Create(string projectId, string type, string message, string? taskId = null, string? chatId = null) =>
        new(DateTimeOffset.UtcNow, projectId, type, message, taskId, chatId);

    public static ProjectActivityEvent InterventionRequired(string projectId, string reason, string? taskId = null, string? chatId = null) =>
        Create(projectId, "INTERVENTION_REQUIRED", reason, taskId, chatId);

    public static ProjectActivityEvent InterventionCleared(string projectId, string message, string? taskId = null, string? chatId = null) =>
        Create(projectId, "INTERVENTION_CLEARED", message, taskId, chatId);
}
