namespace GPTDeskTop.Services;
public sealed record RotationCheckpoint(string ProjectId, string OldChatId, int ChatGeneration, string ContinuationPacket, DateTimeOffset SavedAt, bool Durable)
{
    public static RotationCheckpoint Create(string projectId, string oldChatId, int chatGeneration, string continuationPacket) => new(projectId, oldChatId, chatGeneration, continuationPacket, DateTimeOffset.UtcNow, true);
}
