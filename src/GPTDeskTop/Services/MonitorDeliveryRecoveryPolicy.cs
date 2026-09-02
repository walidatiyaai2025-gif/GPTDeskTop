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
    internal static bool IsMatchingUserTurnEvidence(string? observedText, string? expectedText)
    {
        if (string.Equals(observedText?.Trim(), expectedText?.Trim(), StringComparison.Ordinal))
            return true;

        var observed = NormalizeUserTurnEvidence(observedText);
        var expected = NormalizeUserTurnEvidence(expectedText);
        if (observed.Length == 0 || expected.Length == 0)
            return false;
        if (string.Equals(observed, expected, StringComparison.Ordinal))
            return true;

        const int collapsedPrefixEvidenceLength = 256;
        return expected.Length >= 512
               && observed.Length >= collapsedPrefixEvidenceLength
               && expected.AsSpan(0, collapsedPrefixEvidenceLength)
                   .SequenceEqual(observed.AsSpan(0, collapsedPrefixEvidenceLength));
    }

    private static string NormalizeUserTurnEvidence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var source = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new System.Text.StringBuilder(source.Length);
        var pendingSpace = false;
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(ch));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString().Trim();
    }

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

        if (IsMatchingUserTurnEvidence(observedLastText, expectedText))
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
