namespace GPTDeskTop.Services;

/// <summary>
/// Prevents monitor automation from touching the ChatGPT composer while ChatGPT is still
/// generating or while the composer/send control is transiently disabled. The monitor must
/// observe first and mutate only after this gate reports ReadyToSend.
/// </summary>
public static class ChatComposerInterlockPolicy
{
    public static ComposerAutomationDecision Decide(
        bool isGenerating,
        bool editorPresent,
        bool editorEnabled,
        bool sendButtonPresent,
        bool sendButtonEnabled,
        bool hasRenderedError)
    {
        if (hasRenderedError)
            return ComposerAutomationDecision.DeferForRenderedError;

        if (isGenerating)
            return ComposerAutomationDecision.DeferWhileGenerating;

        if (!editorPresent || !editorEnabled)
            return ComposerAutomationDecision.DeferUntilEditorReady;

        if (!sendButtonPresent || !sendButtonEnabled)
            return ComposerAutomationDecision.DeferUntilSendReady;

        return ComposerAutomationDecision.ReadyToSend;
    }
}

public enum ComposerAutomationDecision
{
    ReadyToSend,
    DeferWhileGenerating,
    DeferUntilEditorReady,
    DeferUntilSendReady,
    DeferForRenderedError
}
