using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Repairs legacy duplicate stable-conversation ownership by moving exactly one
/// duplicate owner to a different stable conversation that is not owned by any
/// other saved monitor. The saved monitor row is updated in place so its local
/// identity, history relationship, operator configuration and rotation state are
/// preserved.
/// </summary>
public sealed class DuplicateOwnershipRepairService
{
    private readonly LocalDatabase _database;

    public DuplicateOwnershipRepairService(LocalDatabase database)
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

        if (!MonitorConversationOwnership.IsDuplicateOwner(monitor.Id, monitors))
            throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");

        if (string.Equals(monitor.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve duplicate ownership.");

        var existingOwner = monitors.FirstOrDefault(saved =>
            saved.Id != monitor.Id
            && string.Equals(saved.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase));
        if (existingOwner is not null)
            throw new InvalidOperationException($"Monitor #{existingOwner.Id} already owns the selected ChatGPT conversation.");

        var rebind = await _database.RebindMonitorConversationIfAvailableAsync(
            monitor.Id,
            monitor.Url ?? string.Empty,
            targetTab.Id,
            targetTab.Title,
            targetTab.Url,
            requireDuplicateSourceOwnership: true,
            diagnosticPrompt: "Monitor ownership repair",
            diagnosticResponse: $"Rebound duplicate owner monitor #{monitor.Id} to a different unowned stable ChatGPT conversation.",
            diagnosticStatus: "MonitorDuplicateConversationOwnershipRebound",
            cancellationToken);

        var pending = string.Equals(
            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),
            "1",
            StringComparison.Ordinal);

        return new MonitorIdentityRebindResult(rebind.MonitorId, rebind.PreviousUrl, rebind.NewUrl, pending);
    }
}
