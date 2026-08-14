using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public static class NewChatStableTargetSelector
{
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
