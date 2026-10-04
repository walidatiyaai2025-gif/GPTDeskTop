using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class NewChatStableTargetSelector
{

    public static ChromeTab? SelectLiveFreshTarget(
        ChromeTab openedTab,
        IReadOnlySet<string> preexistingTargetIds,
        IEnumerable<ChromeTab> liveTabs)
    {
        ArgumentNullException.ThrowIfNull(openedTab);
        ArgumentNullException.ThrowIfNull(preexistingTargetIds);
        ArgumentNullException.ThrowIfNull(liveTabs);

        var tabs = liveTabs.ToList();

        // Prefer the original target whenever it still exists and remains a ChatGPT page.
        // This path is valid both before and after /c/{conversation-id} promotion.
        var sameTarget = tabs.FirstOrDefault(tab =>
            string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal)
            && RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url));
        if (sameTarget is not null)
            return sameTarget;

        // ChatGPT may replace the CDP target while the fresh root shell is loading, before the
        // first physical submit and before a durable conversation URL exists. The pre-create
        // baseline is the ownership proof: only a target absent from that baseline is eligible.
        // If more than one eligible new ChatGPT target exists, fail closed rather than guessing.
        var replacements = tabs
            .Where(tab =>
                RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url)
                && !preexistingTargetIds.Contains(tab.Id)
                && !string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal))
            .ToList();

        return replacements.Count == 1 ? replacements[0] : null;
    }

    public static ChromeTab? Select(
        ChromeTab openedTab,
        IReadOnlySet<string> preexistingTargetIds,
        IEnumerable<ChromeTab> liveTabs)
    {
        ArgumentNullException.ThrowIfNull(openedTab);
        ArgumentNullException.ThrowIfNull(preexistingTargetIds);
        ArgumentNullException.ThrowIfNull(liveTabs);

        var tabs = liveTabs.ToList();

        var sameTarget = tabs.FirstOrDefault(tab =>
            string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal)
            && RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url));
        if (sameTarget is not null)
            return sameTarget;

        // ChatGPT can replace the CDP target while the new-chat shell becomes /c/{id}.
        // Only accept a target that did not exist before this workflow started. If more than
        // one new stable target exists, fail closed instead of attaching to the wrong chat.
        var replacements = tabs
            .Where(tab =>
                RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)
                && !preexistingTargetIds.Contains(tab.Id)
                && !string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal))
            .ToList();

        return replacements.Count == 1 ? replacements[0] : null;
    }
}
