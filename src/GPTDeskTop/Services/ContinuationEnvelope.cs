namespace GPTDeskTop.Services;
public sealed record ContinuationEnvelope(string ProjectId, string ProjectName, string CurrentPhase, string NextAction, int ChatGeneration, string StateFingerprint)
{
    public int NextGeneration => ChatGeneration + 1;
}
