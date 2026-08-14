namespace GPTDeskTop.Services;

/// <summary>
/// Prevents monitor automation from touching the ChatGPT composer while ChatGPT is still
/// generating or while the editor is unavailable. Preparation and submission are deliberately
/// separate gates because ChatGPT normally keeps the Send button disabled while the composer is
/// empty; automation may prepare text only after the editor/generation gate is clear, then must
/// pass the stricter send-enabled gate before any click/Enter mutation.
/// </summary>
public static class ChatComposerInterlockPolicy
{
    public static ComposerAutomationDecision DecideBeforeEditorMutation(
        bool isGenerating,
        bool editorPresent,
        bool editorEnabled,
        bool hasRenderedError)
    {
        if (hasRenderedError)
            return ComposerAutomationDecision.DeferForRenderedError;

        if (isGenerating)
            return ComposerAutomationDecision.DeferWhileGenerating;

        if (!editorPresent || !editorEnabled)
            return ComposerAutomationDecision.DeferUntilEditorReady;

        return ComposerAutomationDecision.ReadyToPrepare;
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

        if (!sendButtonPresent || !sendButtonEnabled)
            return ComposerAutomationDecision.DeferUntilSendReady;

        return ComposerAutomationDecision.ReadyToSend;
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

public enum ComposerAutomationDecision
{
    ReadyToPrepare,
    ReadyToSend,
    DeferWhileGenerating,
    DeferUntilEditorReady,
    DeferUntilSendReady,
    DeferForRenderedError
}
