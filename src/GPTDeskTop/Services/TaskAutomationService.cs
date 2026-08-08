using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Drives the development-plan message queue in bounded work/cooling windows.
/// This is a cooperative rate limiter: it does not attempt to bypass service quotas or access controls.
/// </summary>
public sealed class TaskAutomationService : IAsyncDisposable
{
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly TaskAutomationConfig _config;
    private readonly TaskMessageCatalog _catalog;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;

    public event Action<string>? Activity;
    public bool IsRunning => _worker is { IsCompleted: false };

    public TaskAutomationService(
        ChromeDevToolsService chrome,
        LocalDatabase database,
        TaskAutomationConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;

        var catalogPath = Path.IsPathRooted(config.MessageCatalogFile)
            ? config.MessageCatalogFile
            : Path.Combine(AppContext.BaseDirectory, config.MessageCatalogFile);
        _catalog = TaskMessageCatalog.Load(catalogPath);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_config.Enabled || IsRunning)
                return;

            if (_catalog.Messages.Count == 0)
            {
                Activity?.Invoke("Task automation disabled: message catalog is empty.");
                return;
            }

            _worker = Task.Run(() => RunAsync(_shutdown.Token), _shutdown.Token);
            Activity?.Invoke($"Task automation started: {_config.WorkWindowMinutes}m work / {_config.CoolingWindowMinutes}m cooling.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_worker is null)
                return;

            _shutdown.Cancel();
            try { await _worker; }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            _worker = null;
            Activity?.Invoke("Task automation stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var workMinutes = Math.Clamp(_config.WorkWindowMinutes, 1, 120);
        var coolingMinutes = Math.Clamp(_config.CoolingWindowMinutes, 0, 120);

        // Startup resume: if a checkpoint says the previous cycle was interrupted,
        // continue with the next catalog message rather than resetting the sequence.
        var resumeOnStartup = _config.ResumeOnStartup;
        var cycleStarted = DateTimeOffset.UtcNow;
        var firstCycle = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!firstCycle || !resumeOnStartup)
                cycleStarted = DateTimeOffset.UtcNow;

            firstCycle = false;
            await RunWorkWindowAsync(cycleStarted, TimeSpan.FromMinutes(workMinutes), cancellationToken);

            if (cancellationToken.IsCancellationRequested || coolingMinutes == 0)
                continue;

            await SetPhaseAsync("Cooling", cancellationToken);
            Activity?.Invoke($"Cooling started for {coolingMinutes} minutes.");
            await Task.Delay(TimeSpan.FromMinutes(coolingMinutes), cancellationToken);
            Activity?.Invoke("Cooling completed. Resuming development-plan work.");
        }
    }

    private async Task RunWorkWindowAsync(
        DateTimeOffset cycleStarted,
        TimeSpan workWindow,
        CancellationToken cancellationToken)
    {
        await SetPhaseAsync("Working", cancellationToken);

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
        var targets = monitors
            .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.TabId))
            .ToList();

        if (targets.Count == 0)
        {
            Activity?.Invoke("Work window skipped: no enabled saved ChatGPT monitors with an active Tab ID.");
            await SetPhaseAsync("Idle", cancellationToken);
            return;
        }

        var message = await GetNextMessageAsync(cancellationToken);
        var tabs = await _chrome.GetTabsAsync(cancellationToken);
        var sentCount = 0;

        foreach (var monitor in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - cycleStarted >= workWindow)
                break;

            var tab = tabs.FirstOrDefault(t => string.Equals(t.Id, monitor.TabId, StringComparison.Ordinal));
            if (tab is null)
            {
                await SaveCheckpointAsync(monitor.Id, "TabUnavailable", message, cancellationToken);
                Activity?.Invoke($"[{monitor.Title}] skipped: saved ChatGPT tab is not currently open.");
                continue;
            }

            try
            {
                var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                if (state.IsGenerating)
                {
                    await SaveCheckpointAsync(monitor.Id, "Busy", message, cancellationToken);
                    Activity?.Invoke($"[{monitor.Title}] skipped: ChatGPT is still generating a response.");
                    continue;
                }

                var sent = await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken);
                var status = sent ? "TaskPlanMessageSent" : "TaskPlanMessageFailed";
                await _database.AddLogAsync("Outbound", message, string.Empty, status, monitor.Id, tab.Id, monitor.Title, cancellationToken);
                await SaveCheckpointAsync(monitor.Id, sent ? "Sent" : "SendFailed", message, cancellationToken);

                if (sent)
                {
                    sentCount++;
                    Activity?.Invoke($"[{monitor.Title}] development-plan message sent. Checkpoint saved.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await SaveCheckpointAsync(monitor.Id, "Cancelled", message, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                await SaveCheckpointAsync(monitor.Id, "Error", message, cancellationToken);
                await ExceptionLogService.LogAsync(ex, "TaskAutomationService.Send", monitor.Id, tab.Id, monitor.Title);
                Activity?.Invoke($"[{monitor.Title}] task message failed: {ex.Message}");
            }
        }

        await _database.SetSettingAsync("TaskAutomation.LastCycleCompletedUtc", DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
        await _database.SetSettingAsync("TaskAutomation.LastCycleSentCount", sentCount.ToString(), cancellationToken);
        await SetPhaseAsync("WorkComplete", cancellationToken);
        Activity?.Invoke($"Work window completed. Sent {sentCount}/{targets.Count} development-plan message(s).");
    }

    private async Task<string> GetNextMessageAsync(CancellationToken cancellationToken)
    {
        var raw = await _database.GetSettingAsync("TaskAutomation.MessageIndex", cancellationToken);
        var index = int.TryParse(raw, out var stored) ? Math.Max(0, stored) : 0;
        var message = _catalog.Messages[index % _catalog.Messages.Count];
        await _database.SetSettingAsync("TaskAutomation.MessageIndex", ((index + 1) % _catalog.Messages.Count).ToString(), cancellationToken);
        return message;
    }

    private async Task SaveCheckpointAsync(long monitorId, string status, string message, CancellationToken cancellationToken)
    {
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Status", status, cancellationToken);
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Message", message, cancellationToken);
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Utc", DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }

    private Task SetPhaseAsync(string phase, CancellationToken cancellationToken) =>
        _database.SetSettingAsync("TaskAutomation.Phase", phase, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        catch { }
        _shutdown.Dispose();
        _lifecycle.Dispose();
    }
}
