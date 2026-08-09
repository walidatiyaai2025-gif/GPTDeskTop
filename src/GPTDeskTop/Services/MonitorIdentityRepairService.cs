using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record MonitorIdentityRebindResult(
    long MonitorId,
    string PreviousUrl,
    string NewUrl,
    bool CrashRecoveryPending);

/// <summary>
/// Repairs a legacy saved monitor whose persisted URL is not a stable ChatGPT
/// conversation identity. The existing monitor row is retained so all history,
/// rotation state and operator configuration continue to belong to the same ID.
/// </summary>
public sealed class MonitorIdentityRepairService
{
    private readonly LocalDatabase _database;

    public MonitorIdentityRepairService(LocalDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<MonitorIdentityRebindResult> RebindAsync(
        long monitorId,
        ChromeTab targetTab,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetTab);
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(targetTab.Url))
            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");
        if (string.IsNullOrWhiteSpace(targetTab.Id))
            throw new InvalidOperationException("The selected ChatGPT conversation does not have a usable Chrome target ID.");

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
        var monitor = monitors.SingleOrDefault(saved => saved.Id == monitorId)
            ?? throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");

        if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            throw new InvalidOperationException("This monitor already has a stable ChatGPT conversation identity and does not need repair.");

        var duplicate = monitors.FirstOrDefault(saved =>
            saved.Id != monitor.Id
            && string.Equals(saved.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            throw new InvalidOperationException($"Monitor #{duplicate.Id} already owns the selected ChatGPT conversation.");

        var previousUrl = monitor.Url ?? string.Empty;
        monitor.TabId = targetTab.Id;
        monitor.Title = string.IsNullOrWhiteSpace(targetTab.Title) ? monitor.Title : targetTab.Title;
        monitor.Url = targetTab.Url;

        await _database.SaveMonitorAsync(monitor, cancellationToken);
        await _database.AddLogAsync(
            "System",
            "Monitor identity repair",
            $"Rebound monitor #{monitor.Id} from an invalid saved identity to a stable ChatGPT conversation.",
            "MonitorConversationIdentityRebound",
            monitor.Id,
            monitor.TabId,
            monitor.Title,
            cancellationToken);

        var pending = string.Equals(
            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),
            "1",
            StringComparison.Ordinal);

        return new MonitorIdentityRebindResult(monitor.Id, previousUrl, monitor.Url, pending);
    }
}