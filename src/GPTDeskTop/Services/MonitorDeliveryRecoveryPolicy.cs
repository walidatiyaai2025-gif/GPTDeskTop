using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

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
