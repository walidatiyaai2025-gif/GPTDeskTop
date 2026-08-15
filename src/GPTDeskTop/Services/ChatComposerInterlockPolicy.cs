namespace GPTDeskTop.Services;

/// <summary>
/// Prevents monitor automation from touching the ChatGPT composer while ChatGPT is still
/// generating or while the editor is unavailable. Preparation and submission are deliberately
/// separate gates because ChatGPT normally keeps the Send button disabled while the composer is
/// empty; automation may prepare text only after the editor/generation gate is clear, then must
/// pass the stricter send-enabled gate before any click mutation.
/// </summary>
public static class ChatComposerInterlockPolicy
{
    public static ComposerAutomationDecision DecideBeforeEditorMutation(
        bool isGenerating,
        bool editorPresent,
        bool editorEnabled,
        bool hasRenderedError)
    {
        var decision = hasRenderedError
            ? ComposerAutomationDecision.DeferForRenderedError
            : isGenerating
                ? ComposerAutomationDecision.DeferWhileGenerating
                : !editorPresent || !editorEnabled
                    ? ComposerAutomationDecision.DeferUntilEditorReady
                    : ComposerAutomationDecision.ReadyToPrepare;

        ChatComposerDecisionDiagnostics.Record(decision);
        return decision;
    }

    public static ComposerAutomationDecision DecideBeforeSubmit(
        bool isGenerating,
        bool editorPresent,
        bool editorEnabled,
        bool sendButtonPresent,
        bool sendButtonEnabled,
        bool hasRenderedError)
    {
        var preparation = DecideBeforeEditorMutation(isGenerating, editorPresent, editorEnabled, hasRenderedError);
        if (preparation != ComposerAutomationDecision.ReadyToPrepare)
            return preparation;

        var decision = !sendButtonPresent || !sendButtonEnabled
            ? ComposerAutomationDecision.DeferUntilSendReady
            : ComposerAutomationDecision.ReadyToSend;

        ChatComposerDecisionDiagnostics.Record(decision);
        return decision;
    }

    public static ComposerAutomationDecision Decide(
        bool isGenerating,
        bool editorPresent,
        bool editorEnabled,
        bool sendButtonPresent,
        bool sendButtonEnabled,
        bool hasRenderedError)
        => DecideBeforeSubmit(isGenerating, editorPresent, editorEnabled, sendButtonPresent, sendButtonEnabled, hasRenderedError);
}

/// <summary>
/// Lightweight runtime diagnostics for composer gating. No prompt or conversation text is ever
/// recorded here; only the latest readiness decision, a stable reason code and timestamp.
/// </summary>
public static class ChatComposerDecisionDiagnostics
{
    private static readonly object Sync = new();
    private static ComposerDecisionSnapshot _last = new(
        ComposerAutomationDecision.DeferUntilEditorReady,
        "not-observed",
        DateTimeOffset.MinValue);

    public static ComposerDecisionSnapshot Last
    {
        get { lock (Sync) return _last; }
    }

    internal static void Record(ComposerAutomationDecision decision)
    {
        var reason = decision switch
        {
            ComposerAutomationDecision.ReadyToPrepare => "editor-ready",
            ComposerAutomationDecision.ReadyToSend => "send-ready",
            ComposerAutomationDecision.DeferWhileGenerating => "chatgpt-generating",
            ComposerAutomationDecision.DeferUntilEditorReady => "editor-not-ready",
            ComposerAutomationDecision.DeferUntilSendReady => "send-not-ready",
            ComposerAutomationDecision.DeferForRenderedError => "rendered-error",
            _ => "unknown"
        };

        lock (Sync)
            _last = new ComposerDecisionSnapshot(decision, reason, DateTimeOffset.UtcNow);

        RuntimeFlightRecorder.Record(
            "Composer",
            decision.ToString(),
            decision is ComposerAutomationDecision.ReadyToPrepare or ComposerAutomationDecision.ReadyToSend ? "ready" : "deferred",
            reason);
    }
}

public sealed record ComposerDecisionSnapshot(
    ComposerAutomationDecision Decision,
    string Reason,
    DateTimeOffset ObservedAtUtc);

public enum ComposerAutomationDecision
{
    ReadyToPrepare,
    ReadyToSend,
    DeferWhileGenerating,
    DeferUntilEditorReady,
    DeferUntilSendReady,
    DeferForRenderedError
}
