namespace GPTDeskTop.Services;
public sealed record RotationTransaction(string ProjectId, int FromGeneration, int ToGeneration, ChatRotationStage Stage, DateTimeOffset StartedAt)
{
    public RotationTransaction Advance(ChatRotationStage next) => this with { Stage = next };
}
