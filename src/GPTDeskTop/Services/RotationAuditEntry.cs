namespace GPTDeskTop.Services;
public sealed record RotationAuditEntry(string ProjectId, int Generation, ChatRotationStage Stage, string Message, DateTimeOffset At)
{
    public static RotationAuditEntry Create(string projectId, int generation, ChatRotationStage stage, string message) => new(projectId, generation, stage, message, DateTimeOffset.UtcNow);
}
