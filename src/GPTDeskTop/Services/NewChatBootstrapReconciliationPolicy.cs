namespace GPTDeskTop.Services;

/// <summary>
/// Decides whether a fresh stable ChatGPT conversation is sufficient read-only evidence that the
/// bootstrap user turn was accepted even when the original verified-send call lost its receipt
/// during the new-chat -> /c/{id} target transition.
/// </summary>
public static class NewChatBootstrapReconciliationPolicy
{
    /// <summary>
    /// A target is eligible only when it is a stable conversation created after this workflow
    /// started. On that fresh target, assistant activity (streaming, a rendered assistant turn,
    /// or a rendered response error) proves that ChatGPT started processing the bootstrap turn.
    /// This method never authorizes another physical send.
    /// </summary>
    public static bool CanConfirmAcceptedBootstrap(
        bool isStableConversation,
        bool targetExistedBeforeWorkflow,
        int assistantCount,
        bool isGenerating,
        bool hasRenderedError)
        => isStableConversation
           && !targetExistedBeforeWorkflow
           && (assistantCount > 0 || isGenerating || hasRenderedError);
}
