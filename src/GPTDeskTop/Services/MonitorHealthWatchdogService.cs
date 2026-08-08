using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

/// <summary>
/// Background health supervisor foundation for GPTDeskTop monitors.
/// Keeps recovery decisions separate from the UI thread and avoids aggressive restart loops.
/// </summary>
public sealed class MonitorHealthWatchdogService
{
    private readonly LocalDatabase _database;
    private readonly ChatGptMonitorService _monitor;
    private readonly TimeSpan _pollInterval;

    public MonitorHealthWatchdogService(
        LocalDatabase database,
        ChatGptMonitorService monitor,
        TimeSpan? pollInterval = null)
    {
        _database = database;
        _monitor = monitor;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ExceptionLogService.LogAsync(ex, "MonitorHealthWatchdogService.RunAsync");
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
        var enabled = monitors.Count(x => x.Enabled);
        await _database.SetSettingAsync("LastHealthWatchdogUtc", DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
        await _database.SetSettingAsync("EnabledMonitorCount", enabled.ToString(), cancellationToken);
    }
}
