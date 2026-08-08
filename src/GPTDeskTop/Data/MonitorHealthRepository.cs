using GPTDeskTop.Models;

namespace GPTDeskTop.Data;

/// <summary>
/// Persistence abstraction for monitor heartbeat and recovery state.
/// The implementation is intentionally small so it can be used by both the
/// watchdog and monitor workers without coupling them to the UI.
/// </summary>
public sealed class MonitorHealthRepository
{
    private readonly LocalDatabase _database;

    public MonitorHealthRepository(LocalDatabase database)
    {
        _database = database;
    }

    public Task UpdateHeartbeatAsync(
        int monitorId,
        string? tabId,
        CancellationToken cancellationToken = default)
    {
        return _database.SetSettingAsync(
            $"MonitorHealth:{monitorId}:Heartbeat",
            $"{DateTimeOffset.UtcNow:O}|{tabId}",
            cancellationToken);
    }

    public Task SetStatusAsync(
        int monitorId,
        string status,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        return _database.SetSettingAsync(
            $"MonitorHealth:{monitorId}:Status",
            $"{status}|{error}",
            cancellationToken);
    }
}
