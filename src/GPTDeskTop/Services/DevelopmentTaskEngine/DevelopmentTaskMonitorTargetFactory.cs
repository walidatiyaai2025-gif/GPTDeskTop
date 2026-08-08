using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Builds live development-plan recipients from the persisted monitor registry.
/// Resolution happens at the beginning of a delivery window, so the same logical
/// conversation is reused after Cooling or process restart whenever its saved URL
/// is still open in Chrome.
/// </summary>
public sealed class DevelopmentTaskMonitorTargetFactory
{
    private readonly LocalDatabase _database;
    private readonly SavedMonitorTabResolver _resolver;
    private readonly ChromeDevToolsService _chrome;

    public DevelopmentTaskMonitorTargetFactory(
        LocalDatabase database,
        SavedMonitorTabResolver resolver,
        ChromeDevToolsService chrome)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
    }

    public async Task<IReadOnlyList<DevelopmentTaskMonitorRecipient>> ResolveEnabledRecipientsAsync(
        CancellationToken cancellationToken = default)
    {
        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var recipients = new List<DevelopmentTaskMonitorRecipient>();

        foreach (var monitor in monitors.Where(x => x.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var optedIn = await _database.GetSettingAsync(
                $"TaskAutomation.Monitor.{monitor.Id}.Enabled",
                cancellationToken).ConfigureAwait(false);
            if (!IsOptedIn(optedIn))
                continue;

            var resolution = SavedMonitorTabResolver.Resolve(monitor, tabs);
            if (!resolution.Found || resolution.Tab is null)
            {
                await _database.AddLogAsync(
                    "System",
                    string.Empty,
                    resolution.Reason,
                    "DevelopmentMonitorTabUnavailable",
                    monitor.Id,
                    monitor.TabId,
                    monitor.Title,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var monitorId = monitor.Id.ToString();
            var tab = resolution.Tab;
            recipients.Add(new DevelopmentTaskMonitorRecipient(
                monitorId,
                tab.Id,
                message => SendVerifiedAsync(tab, message)));

            await _database.AddLogAsync(
                "System",
                resolution.MatchType,
                tab.Url,
                "DevelopmentMonitorRebound",
                monitor.Id,
                tab.Id,
                monitor.Title,
                cancellationToken).ConfigureAwait(false);
        }

        return recipients;
    }

    private async Task<bool> SendVerifiedAsync(ChromeTab tab, string message)
    {
        var state = await _chrome.GetChatStateAsync(tab).ConfigureAwait(false);
        if (state.IsGenerating || !string.IsNullOrWhiteSpace(state.ErrorText))
            return false;
        return await _chrome.SendChatMessageVerifiedAsync(tab, message).ConfigureAwait(false);
    }

    private static bool IsOptedIn(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
