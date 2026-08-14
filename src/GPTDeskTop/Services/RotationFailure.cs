namespace GPTDeskTop.Services;
public sealed record RotationFailure(ChatRotationStage Stage, string Reason, DateTimeOffset At, bool OldChatPreserved)
{
    public static RotationFailure Safe(ChatRotationStage stage, string reason) => new(stage, reason, DateTimeOffset.UtcNow, true);
}
