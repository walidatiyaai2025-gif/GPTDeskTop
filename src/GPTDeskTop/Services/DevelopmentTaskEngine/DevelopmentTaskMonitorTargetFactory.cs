using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Builds live development-plan recipients from the persisted monitor registry.
/// Opted-in enabled monitors are started on demand when a live conversation target
/// is available, so Development Messages Start is a real one-click execution path.
/// </summary>
public sealed class DevelopmentTaskMonitorTargetFactory
{
    private readonly LocalDatabase _database;
    private readonly SavedMonitorTabResolver _resolver;
    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService? _monitorService;
    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();

    public DevelopmentTaskMonitorTargetFactory(
        LocalDatabase database,
        SavedMonitorTabResolver resolver,
        ChromeDevToolsService chrome,
        ChatGptMonitorService? monitorService = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitorService = monitorService;
        DevelopmentPlanMonitorSettings.ConfigureDatabase(_database);
    }

    public async Task<IReadOnlyList<DevelopmentTaskMonitorRecipient>> ResolveEnabledRecipientsAsync(
        CancellationToken cancellationToken = default)
    {
        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        var duplicateMonitorIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);
        var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var recipients = new List<DevelopmentTaskMonitorRecipient>();

        foreach (var monitor in monitors.Where(x => x.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (duplicateMonitorIds.Contains(monitor.Id))
            {
                await _database.AddLogAsync(
                    "System", string.Empty,
                    "Saved monitor conversation ownership is ambiguous. Resolve the duplicate monitor rows before development delivery can target this conversation.",
                    "DevelopmentMonitorDuplicateConversationOwnership", monitor.Id,
                    monitor.TabId, monitor.Title, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var optedIn = await DevelopmentPlanMonitorSettings.IsEnabledAsync(
                _database, monitor, cancellationToken).ConfigureAwait(false);
            monitor.UseDevelopmentMessages = optedIn;
            if (!optedIn)
                continue;

            var resolution = SavedMonitorTabResolver.Resolve(monitor, tabs);
            if (!resolution.Found || resolution.Tab is null)
            {
                await _database.AddLogAsync(
                    "System", string.Empty, resolution.Reason,
                    "DevelopmentMonitorTabUnavailable", monitor.Id,
                    monitor.TabId, monitor.Title, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var monitorId = monitor.Id.ToString();
            var tab = resolution.Tab;

            if (string.Equals(resolution.MatchType, "PersistedConversationUrl", StringComparison.Ordinal)
                && !string.Equals(monitor.TabId, tab.Id, StringComparison.Ordinal))
            {
                monitor.TabId = tab.Id;
                monitor.Url = tab.Url;
                await _database.SaveMonitorAsync(monitor, cancellationToken).ConfigureAwait(false);

                await _database.AddLogAsync(
                    "System", "PersistedConversationUrl", tab.Url,
                    "DevelopmentMonitorTargetIdUpdated", monitor.Id,
                    tab.Id, monitor.Title, cancellationToken).ConfigureAwait(false);
            }

            if (_monitorService is not null && !_monitorService.IsMonitorRunning(monitor.Id))
            {
                await _monitorService.StartMonitorAsync(monitor, tab).ConfigureAwait(false);
                if (!_monitorService.IsMonitorRunning(monitor.Id))
                {
                    await _database.AddLogAsync(
                        "System", string.Empty,
                        "The opted-in monitor could not be started, so Development Messages will not send a prompt that cannot receive a stable response event.",
                        "DevelopmentMonitorStartUnavailable", monitor.Id,
                        tab.Id, monitor.Title, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            recipients.Add(new DevelopmentTaskMonitorRecipient(
                monitorId,
                tab.Id,
                message => SendVerifiedAsync(monitor.Id, tab, message)));

            await _database.AddLogAsync(
                "System", resolution.MatchType, tab.Url,
                "DevelopmentMonitorRebound", monitor.Id,
                tab.Id, monitor.Title, cancellationToken).ConfigureAwait(false);
        }

        return recipients;
    }

    private async Task<bool> SendVerifiedAsync(long monitorId, ChromeTab tab, string message)
    {
        var state = await _chrome.GetChatStateAsync(tab).ConfigureAwait(false);
        if (state.IsGenerating || !string.IsNullOrWhiteSpace(state.ErrorText))
            return false;

        return await _outboundDelivery.SendOnceAsync(
            monitorId,
            string.IsNullOrWhiteSpace(tab.Url) ? tab.Id : tab.Url,
            message,
            () => _chrome.SendChatMessageVerifiedAsync(tab, message),
            null,
            CancellationToken.None).ConfigureAwait(false);
    }
}
