namespace GPTDeskTop.Services;
public sealed record ChatGenerationState(int Generation, string ChatId, DateTimeOffset StartedAt, string Status)
{
    public ChatGenerationState Next(string newChatId) => new(checked(Generation + 1), newChatId, DateTimeOffset.UtcNow, "ACTIVE");
}
