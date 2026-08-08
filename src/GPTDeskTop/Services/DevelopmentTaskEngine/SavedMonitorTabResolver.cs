using GPTDeskTop.Models;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Rebinds a persisted SavedMonitor to the same logical ChatGPT page after
/// cooling or a normal application restart. Exact DevTools target ID wins;
/// the saved conversation URL is the safe fallback when Chrome recreated the
/// target and assigned a new target ID. Title is intentionally not used as a
/// binding key because titles are not unique.
/// </summary>
public sealed class SavedMonitorTabResolver
{
    private readonly ChromeDevToolsService _chrome;

    public SavedMonitorTabResolver(ChromeDevToolsService chrome)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
    }

    public async Task<SavedMonitorTabResolution> ResolveAsync(
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var resolution = Resolve(monitor, tabs);
        return resolution;
    }

    public static SavedMonitorTabResolution Resolve(
        SavedMonitor monitor,
        IReadOnlyCollection<ChromeTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(tabs);

        if (!string.IsNullOrWhiteSpace(monitor.TabId))
        {
            var exact = tabs.FirstOrDefault(tab =>
                string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal));
            if (exact is not null)
                return SavedMonitorTabResolution.Found(exact, "PersistedTabId");
        }

        if (!string.IsNullOrWhiteSpace(monitor.Url))
        {
            var sameConversation = tabs.FirstOrDefault(tab =>
                string.Equals(NormalizeUrl(tab.Url), NormalizeUrl(monitor.Url), StringComparison.Ordinal));
            if (sameConversation is not null)
                return SavedMonitorTabResolution.Found(sameConversation, "PersistedConversationUrl");
        }

        return SavedMonitorTabResolution.Missing(
            string.IsNullOrWhiteSpace(monitor.TabId) && string.IsNullOrWhiteSpace(monitor.Url)
                ? "No persisted tab identity or conversation URL is available."
                : "The persisted ChatGPT conversation is not currently open.");
    }

    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return value.Trim().TrimEnd('/');
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;
    }
}

public sealed record SavedMonitorTabResolution(
    bool Found,
    ChromeTab? Tab,
    string MatchType,
    string Reason)
{
    public static SavedMonitorTabResolution Found(ChromeTab tab, string matchType)
        => new(true, tab, matchType, string.Empty);

    public static SavedMonitorTabResolution Missing(string reason)
        => new(false, null, "None", reason);
}
