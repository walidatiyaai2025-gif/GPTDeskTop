namespace GPTDeskTop.Services;
public sealed record ContinuationReceipt(bool ProjectOk, bool GenerationOk, bool FingerprintOk, bool ComposerReady)
{
    public bool Valid => ProjectOk && GenerationOk && FingerprintOk && ComposerReady;
}
