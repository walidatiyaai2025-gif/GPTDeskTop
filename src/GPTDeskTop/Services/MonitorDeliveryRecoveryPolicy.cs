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
