using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Executes the editable development-plan message catalog in bounded work/cooling windows.
/// State is persisted before and after delivery so a restart resumes the same monitors
/// from their next undelivered message. This is cooperative pacing and does not bypass
/// external quotas, access controls, or platform protections.
/// </summary>
public sealed class TaskAutomationService : IAsyncDisposable
{
    private const string PhaseKey = "TaskAutomation.Phase";
    private const string WorkStartedKey = "TaskAutomation.WorkWindowStartedUtc";
    private const string CoolingStartedKey = "TaskAutomation.CoolingStartedUtc";
    private const string LastCycleKey = "TaskAutomation.LastCycleCompletedUtc";
    private const string LastSentCountKey = "TaskAutomation.LastCycleSentCount";

    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly TaskAutomationConfig _config;
    private readonly TaskMessageCatalog _catalog;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCts;
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
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_config.Enabled || IsRunning)
                return;

            if (_catalog.Messages.Count == 0)
            {
                Activity?.Invoke("Task automation disabled: message catalog is empty.");
                return;
            }

            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = RunAsync(_runCts.Token);
            Activity?.Invoke($"Task automation started: {_config.WorkWindowMinutes}m work / {_config.CoolingWindowMinutes}m cooling.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_worker is null)
                return;

            _runCts?.Cancel();
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_runCts?.IsCancellationRequested == true) { }
            finally
            {
                _worker = null;
                _runCts?.Dispose();
                _runCts = null;
            }

            // Preserve Working/Cooling timestamps so the next startup can resume
            // the persisted phase instead of resetting the schedule.
            Activity?.Invoke("Task automation stopped for application shutdown; checkpoint state preserved.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var workWindow = TimeSpan.FromMinutes(Math.Clamp(_config.WorkWindowMinutes, 1, 120));
        var coolingWindow = TimeSpan.FromMinutes(Math.Clamp(_config.CoolingWindowMinutes, 0, 120));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var phase = await _database.GetSettingAsync(PhaseKey, cancellationToken).ConfigureAwait(false);

                if (string.Equals(phase, "Cooling", StringComparison.OrdinalIgnoreCase))
                {
                    await ResumeCoolingAsync(coolingWindow, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var workStarted = await GetTimestampAsync(WorkStartedKey, cancellationToken).ConfigureAwait(false);
                if (workStarted is null)
                {
                    workStarted = DateTimeOffset.UtcNow;
                    await _database.SetSettingAsync(WorkStartedKey, workStarted.Value.ToString("O"), cancellationToken).ConfigureAwait(false);
                }

                if (DateTimeOffset.UtcNow - workStarted.Value >= workWindow)
                {
                    await BeginCoolingAsync(cancellationToken).ConfigureAwait(false);
                    if (coolingWindow > TimeSpan.Zero)
                        continue;

                    await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
                }

                await RunOneWorkCycleAsync(workStarted.Value, workWindow, cancellationToken).ConfigureAwait(false);

                // A work cycle contains one logical development-plan message per monitor.
                // The monitor's own message index advances only after verified delivery.
                var refreshedStart = await GetTimestampAsync(WorkStartedKey, cancellationToken).ConfigureAwait(false);
                if (refreshedStart is null || DateTimeOffset.UtcNow - refreshedStart.Value >= workWindow)
                {
                    await BeginCoolingAsync(cancellationToken).ConfigureAwait(false);
                    if (coolingWindow > TimeSpan.Zero)
                        continue;
                    await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Do not send another development-plan message in the same work window.
                    // Sleep until the window ends while remaining restart/cancellation safe.
                    var remaining = workWindow - (DateTimeOffset.UtcNow - refreshedStart.Value);
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown/pause path.
        }
        catch (Exception ex)
        {
            await _database.SetSettingAsync(PhaseKey, "Faulted", CancellationToken.None).ConfigureAwait(false);
            await _database.SetSettingAsync("TaskAutomation.LastError", ex.Message, CancellationToken.None).ConfigureAwait(false);
            await ExceptionLogService.LogAsync(ex, "TaskAutomationService.Run");
            Activity?.Invoke($"Task automation faulted: {ex.Message}");
        }
    }

    private async Task RunOneWorkCycleAsync(
        DateTimeOffset workStarted,
        TimeSpan workWindow,
        CancellationToken cancellationToken)
    {
        await _database.SetSettingAsync(PhaseKey, "Working", cancellationToken).ConfigureAwait(false);

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        var targets = monitors
            .Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.TabId))
            .ToList();

        if (targets.Count == 0)
        {
            Activity?.Invoke("Work window active: no enabled saved ChatGPT monitors with an active Tab ID.");
            return;
        }

        var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var sentCount = 0;

        foreach (var monitor in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - workStarted >= workWindow)
                break;

            var tab = tabs.FirstOrDefault(t => string.Equals(t.Id, monitor.TabId, StringComparison.Ordinal));
            var index = await GetMonitorMessageIndexAsync(monitor.Id, cancellationToken).ConfigureAwait(false);
            var message = BuildMessage(index);

            if (tab is null)
            {
                await SaveCheckpointAsync(monitor.Id, index, "TabUnavailable", message, cancellationToken).ConfigureAwait(false);
                Activity?.Invoke($"[{monitor.Title}] checkpoint retained: saved ChatGPT tab is not open.");
                continue;
            }

            try
            {
                var state = await _chrome.GetChatStateAsync(tab, cancellationToken).ConfigureAwait(false);
                if (state.IsGenerating)
                {
                    await SaveCheckpointAsync(monitor.Id, index, "Busy", message, cancellationToken).ConfigureAwait(false);
                    Activity?.Invoke($"[{monitor.Title}] checkpoint retained: ChatGPT is generating.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(state.ErrorText))
                {
                    await SaveCheckpointAsync(monitor.Id, index, "ChatError", message, cancellationToken).ConfigureAwait(false);
                    Activity?.Invoke($"[{monitor.Title}] checkpoint retained: ChatGPT reported an error.");
                    continue;
                }

                var sent = await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken).ConfigureAwait(false);
                var status = sent ? "TaskPlanMessageSent" : "TaskPlanMessageFailed";
                await _database.AddLogAsync("Outbound", message, string.Empty, status, monitor.Id, tab.Id, monitor.Title, cancellationToken).ConfigureAwait(false);

                if (sent)
                {
                    var nextIndex = (index + 1) % _catalog.Messages.Count;
                    await SaveCheckpointAsync(monitor.Id, index, "Sent", message, cancellationToken, nextIndex).ConfigureAwait(false);
                    sentCount++;
                    Activity?.Invoke($"[{monitor.Title}] message {index + 1}/{_catalog.Messages.Count} delivered; next={nextIndex + 1}.");
                }
                else
                {
                    await SaveCheckpointAsync(monitor.Id, index, "SendFailed", message, cancellationToken).ConfigureAwait(false);
                    Activity?.Invoke($"[{monitor.Title}] delivery failed; message index {index + 1} retained for retry.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await SaveCheckpointAsync(monitor.Id, index, "Cancelled", message, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await SaveCheckpointAsync(monitor.Id, index, "Error", message, cancellationToken).ConfigureAwait(false);
                await ExceptionLogService.LogAsync(ex, "TaskAutomationService.Send", monitor.Id, tab.Id, monitor.Title).ConfigureAwait(false);
                Activity?.Invoke($"[{monitor.Title}] task message failed; checkpoint retained.");
            }
        }

        await _database.SetSettingAsync(LastCycleKey, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync(LastSentCountKey, sentCount.ToString(), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync("TaskAutomation.LastCatalogCount", _catalog.Messages.Count.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private string BuildMessage(int index)
    {
        var template = _catalog.Messages[index % _catalog.Messages.Count];
        return template
            .Replace("{planId}", "default-development-plan", StringComparison.OrdinalIgnoreCase)
            .Replace("{planTitle}", "GPTDeskTop Development Plan", StringComparison.OrdinalIgnoreCase)
            .Replace("{step}", (index + 1).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{total}", _catalog.Messages.Count.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetMonitorMessageIndexAsync(long monitorId, CancellationToken cancellationToken)
    {
        var raw = await _database.GetSettingAsync($"TaskAutomation.Monitor.{monitorId}.NextMessageIndex", cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(raw, out var index))
            index = 0;
        return Math.Clamp(index, 0, Math.Max(0, _catalog.Messages.Count - 1));
    }

    private async Task SaveCheckpointAsync(
        long monitorId,
        int messageIndex,
        string status,
        string message,
        CancellationToken cancellationToken,
        int? nextIndex = null)
    {
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Status", status, cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.MessageIndex", messageIndex.ToString(), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Message", message, cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitorId}.Utc", DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);

        if (nextIndex.HasValue)
            await _database.SetSettingAsync($"TaskAutomation.Monitor.{monitorId}.NextMessageIndex", nextIndex.Value.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task BeginCoolingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _database.SetSettingAsync(PhaseKey, "Cooling", cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync(CoolingStartedKey, now.ToString("O"), cancellationToken).ConfigureAwait(false);
        Activity?.Invoke("Task automation entered Cooling.");
    }

    private async Task ResumeCoolingAsync(TimeSpan coolingWindow, CancellationToken cancellationToken)
    {
        if (coolingWindow <= TimeSpan.Zero)
        {
            await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var started = await GetTimestampAsync(CoolingStartedKey, cancellationToken).ConfigureAwait(false) ?? DateTimeOffset.UtcNow;
        var remaining = coolingWindow - (DateTimeOffset.UtcNow - started);
        if (remaining > TimeSpan.Zero)
        {
            Activity?.Invoke($"Cooling resumed; {Math.Ceiling(remaining.TotalSeconds)} seconds remaining.");
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }

        await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task BeginNewWorkWindowAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _database.SetSettingAsync(PhaseKey, "Working", cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync(WorkStartedKey, now.ToString("O"), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync(CoolingStartedKey, string.Empty, cancellationToken).ConfigureAwait(false);
        Activity?.Invoke("Task automation resumed in a new work window.");
    }

    private async Task<DateTimeOffset?> GetTimestampAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await _database.GetSettingAsync(key, cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { }
        _lifecycle.Dispose();
    }
}
