using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

internal enum PostRefreshUserTurnObservation
{
    Hydrating,
    StableBaseline,
    ReceiptConfirmed,
    UnexpectedChange
}

internal static class MonitorDeliveryRecoveryPolicy
{
    internal static bool CanReuseMatchingUserTailAsReceipt(
        bool requireNewTurn,
        int userMessageCount,
        int assistantMessageCount,
        bool isGenerating)
    {
        if (requireNewTurn) return false;

        // A matching user tail is only a receipt for the current delivery while ChatGPT is
        // still answering it, or before an assistant turn exists for it. Once a completed
        // assistant turn exists, the same text (for example "كمل") must be allowed as a new turn.
        return isGenerating || assistantMessageCount < userMessageCount;
    }

    internal static PostRefreshUserTurnObservation ClassifyPostRefreshUserTurn(
        bool snapshotReadable,
        int baselineUserTurnCount,
        int observedUserTurnCount,
        string observedLastText,
        string expectedText)
    {
        if (!snapshotReadable || observedUserTurnCount < baselineUserTurnCount)
            return PostRefreshUserTurnObservation.Hydrating;

        if (observedUserTurnCount == baselineUserTurnCount)
            return PostRefreshUserTurnObservation.StableBaseline;

        if (string.Equals(observedLastText, expectedText, StringComparison.Ordinal))
            return PostRefreshUserTurnObservation.ReceiptConfirmed;

        // Immediately after Page.reload ChatGPT can expose the message nodes before their text is
        // hydrated. Empty tail text is therefore not evidence that another user turn replaced the
        // pending continuation.
        return string.IsNullOrWhiteSpace(observedLastText)
            ? PostRefreshUserTurnObservation.Hydrating
            : PostRefreshUserTurnObservation.UnexpectedChange;
    }

    internal static bool IsFreshChatPromotion(string? submitOriginUrl, string? reboundUrl)
    {
        if (!RuntimeHealthPresentation.IsChatGptTabUrl(submitOriginUrl)
            || RuntimeHealthPresentation.IsChatGptConversationUrl(submitOriginUrl)
            || !RuntimeHealthPresentation.IsChatGptConversationUrl(reboundUrl))
            return false;

        if (!Uri.TryCreate(submitOriginUrl, UriKind.Absolute, out var origin))
            return false;

        // Monitor Only creates a fresh ChatGPT target at the site root. A successful first submit
        // promotes that exact target to /c/{conversation-id}. The caller must rebind by exact target
        // ID before using this signal, so an unrelated pre-existing conversation can never satisfy it.
        return string.IsNullOrWhiteSpace(origin.AbsolutePath.Trim('/'));
    }

    internal static bool IsDeliveryBindingAllowed(string targetId, string originUrl, string liveId, string liveUrl)
    {
        if (RuntimeHealthPresentation.IsChatGptConversationUrl(originUrl))
            return ChatGptConversationIdentity.IsSame(originUrl, liveUrl);
        return string.Equals(targetId, liveId, StringComparison.Ordinal)
            && (string.Equals(originUrl, liveUrl, StringComparison.Ordinal)
                || IsFreshChatPromotion(originUrl, liveUrl));
    }

    internal static ChromeTab? FindBestBinding(IReadOnlyCollection<ChromeTab> liveTabs, ChromeTab trackedTab)
    {
        ArgumentNullException.ThrowIfNull(liveTabs);
        ArgumentNullException.ThrowIfNull(trackedTab);

        var exact = liveTabs.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, trackedTab.Id, StringComparison.Ordinal));
        if (exact is not null) return exact;

        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(trackedTab.Url))
            return null;

        return liveTabs.FirstOrDefault(candidate =>
            RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)
            && ChatGptConversationIdentity.IsSame(trackedTab.Url, candidate.Url));
    }
}
