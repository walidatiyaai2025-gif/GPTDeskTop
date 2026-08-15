namespace GPTDeskTop.Services;

/// <summary>
/// Immutable read-only view of the ChatGPT composer state used to decide whether automation may
/// prepare/submit text and whether a post-generation Send control is genuinely wedged.
/// No prompt or conversation content is retained in this snapshot.
/// </summary>
public sealed record ComposerReadinessSnapshot(
    bool IsGenerating,
    bool EditorPresent,
    bool EditorEnabled,
    bool SendButtonPresent,
    bool SendButtonEnabled,
    bool HasRenderedError)
{
    public bool IsPostGenerationSendBlocked
        => !IsGenerating
           && EditorPresent
           && EditorEnabled
           && !HasRenderedError
           && (!SendButtonPresent || !SendButtonEnabled);
}

/// <summary>
/// Escalates only a stable post-generation composer wedge. Generating, rendered-error, manual-edit
/// and already-refreshed states never request a reload.
/// </summary>
public static class StuckComposerRecoveryPolicy
{
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(5);

    public static bool ShouldRefresh(
        ComposerReadinessSnapshot snapshot,
        bool editorMatchesExpectedAutomationText,
        TimeSpan blockedFor,
        bool refreshAlreadyUsed)
        => !refreshAlreadyUsed
           && editorMatchesExpectedAutomationText
           && snapshot.IsPostGenerationSendBlocked
           && blockedFor >= DefaultThreshold;
}
