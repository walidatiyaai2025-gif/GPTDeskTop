namespace GPTDeskTop.Services;
public sealed record GenerationProgressSample(DateTimeOffset Timestamp, int AssistantTextLength, string LastActivity, bool IsGenerating, string? ExternalEvidenceFingerprint)
{
    public bool HasProgressComparedWith(GenerationProgressSample previous) => AssistantTextLength > previous.AssistantTextLength || !string.Equals(LastActivity, previous.LastActivity, StringComparison.Ordinal) || !string.Equals(ExternalEvidenceFingerprint, previous.ExternalEvidenceFingerprint, StringComparison.Ordinal);
}
