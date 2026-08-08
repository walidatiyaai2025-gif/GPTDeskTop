using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Executes the editable development-plan message catalog in bounded work/cooling windows.
/// State is persisted before and after delivery so a restart resumes the same monitors
/// from their next undelivered message. Development-plan automation is explicitly opt-in
/// per saved monitor through AppSettings. This is cooperative pacing and does not bypass
/// external quotas, access controls, or platform protections.
/// </summary>
public sealed class TaskAutomationService : IAsyncDisposable
{
    private const string PhaseKey = "TaskAutomation.Phase";
    private const string WorkStartedKey = "TaskAutomation.WorkWindowStartedUtc";
    private const string CoolingStartedKey = "TaskAutomation.CoolingStartedUtc";
    private const string LastCycleKey = "TaskAutomation.LastCycleCompletedUtc";
    private const string LastSentCountKey = "TaskAutomation.LastCycleSentCount";
    private const string CurrentMonitorKey = "TaskAutomation.CurrentMonitorId";
    private const string CurrentMessageKey = "TaskAutomation.CurrentMessage";
    private const string NextMessageKey = "TaskAutomation.NextMessage";

    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly TaskAutomationConfig _config;
    private readonly string _catalogPath;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCts;
    private Task? _worker;

    public event Action<string>? Activity;
    public bool IsRunning => _worker is { IsCompleted: false };

    public TaskAutomationService(ChromeDevToolsService chrome, LocalDatabase database, TaskAutomationConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;
        _catalogPath = Path.IsPathRooted(config.MessageCatalogFile)
            ? config.MessageCatalogFile
            : Path.Combine(AppContext.BaseDirectory, config.MessageCatalogFile);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_config.Enabled || IsRunning) return;
            var catalog = LoadCatalog();
            if (catalog.Messages.Count == 0)
            {
                Activity?.Invoke("Task automation disabled: message catalog is empty.");
                return;
            }
            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = RunAsync(_runCts.Token);
            Activity?.Invoke($"Task automation started: {_config.WorkWindowMinutes}m work / {_config.CoolingWindowMinutes}m cooling.");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_worker is null) return;
            _runCts?.Cancel();
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_runCts?.IsCancellationRequested == true) { }
            finally
            {
                _worker = null;
                _runCts?.Dispose();
                _runCts = null;
            }
            Activity?.Invoke("Task automation stopped; checkpoint state preserved.");
        }
        finally { _lifecycle.Release(); }
    }

    private TaskMessageCatalog LoadCatalog() => TaskMessageCatalog.Load(_catalogPath);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var workWindow = TimeSpan.FromMinutes(Math.Clamp(_config.WorkWindowMinutes, 1, 120));
        var coolingWindow = TimeSpan.FromMinutes(Math.Clamp(_config.CoolingWindowMinutes, 0, 120));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var phase = await _database.GetSettingAsync(PhaseKey, cancellationToken).ConfigureAwait(false);
                if (string.Equals(phase, "Paused", StringComparison.OrdinalIgnoreCase) || string.Equals(phase, "Stopped", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
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
                    if (coolingWindow > TimeSpan.Zero) continue;
                    await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
                }

                await RunOneWorkCycleAsync(workStarted.Value, workWindow, cancellationToken).ConfigureAwait(false);
                var refreshedStart = await GetTimestampAsync(WorkStartedKey, cancellationToken).ConfigureAwait(false);
                if (refreshedStart is null || DateTimeOffset.UtcNow - refreshedStart.Value >= workWindow)
                {
                    await BeginCoolingAsync(cancellationToken).ConfigureAwait(false);
                    if (coolingWindow > TimeSpan.Zero) continue;
                    await BeginNewWorkWindowAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var remaining = workWindow - (DateTimeOffset.UtcNow - refreshedStart.Value);
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await _database.SetSettingAsync(PhaseKey, "Faulted", CancellationToken.None).ConfigureAwait(false);
            await _database.SetSettingAsync("TaskAutomation.LastError", ex.Message, CancellationToken.None).ConfigureAwait(false);
            await ExceptionLogService.LogAsync(ex, "TaskAutomationService.Run");
            Activity?.Invoke($"Task automation faulted: {ex.Message}");
        }
    }

    private async Task RunOneWorkCycleAsync(DateTimeOffset workStarted, TimeSpan workWindow, CancellationToken cancellationToken)
    {
        await _database.SetSettingAsync(PhaseKey, "Working", cancellationToken).ConfigureAwait(false);
        var catalog = LoadCatalog();
        if (catalog.Messages.Count == 0) return;

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        var targets = new List<SavedMonitor>();
        foreach (var monitor in monitors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var optedIn = await IsDevelopmentAutomationEnabledAsync(monitor.Id, cancellationToken).ConfigureAwait(false);
            if (DevelopmentAutomationTargetPolicy.IsEligible(monitor, optedIn))
                targets.Add(monitor);
        }

        if (targets.Count == 0)
        {
            await _database.SetSettingAsync(CurrentMonitorKey, string.Empty, cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync(CurrentMessageKey, "No monitor is opted in to Development Automation.", cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync(NextMessageKey, string.Empty, cancellationToken).ConfigureAwait(false);
            Activity?.Invoke("Work window active: no eligible Development Automation target.");
            return;
        }

        var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var sentCount = 0;
        foreach (var monitor in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - workStarted >= workWindow) break;

            var tab = tabs.FirstOrDefault(t => string.Equals(t.Id, monitor.TabId, StringComparison.Ordinal));
            var index = await GetMonitorMessageIndexAsync(monitor.Id, catalog.Messages.Count, cancellationToken).ConfigureAwait(false);
            var planId = await GetMonitorSettingAsync(monitor.Id, "PlanId", monitor.DevelopmentPlanId, cancellationToken).ConfigureAwait(false);
            var planTitle = await GetMonitorSettingAsync(monitor.Id, "PlanTitle", monitor.DevelopmentPlanTitle, cancellationToken).ConfigureAwait(false);
            var message = BuildMessage(catalog, index, planId, planTitle);
            var nextMessage = BuildMessage(catalog, (index + 1) % catalog.Messages.Count, planId, planTitle);

            await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitor.Id}.CurrentMessage", message, cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync($"TaskAutomation.Checkpoint.{monitor.Id}.NextMessage", nextMessage, cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync(CurrentMonitorKey, monitor.Id.ToString(), cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync(CurrentMessageKey, message, cancellationToken).ConfigureAwait(false);
            await _database.SetSettingAsync(NextMessageKey, nextMessage, cancellationToken).ConfigureAwait(false);

            if (tab is null)
            {
                await SaveCheckpointAsync(monitor.Id, index, "TabUnavailable", message, cancellationToken).ConfigureAwait(false);
                Activity?.Invoke($"[{monitor.Title}] checkpoint retained: saved ChatGPT tab is not open.");
                continue;
            }

            try
            {
                var state = await _chrome.GetChatStateAsync(tab, cancellationToken).ConfigureAwait(false);
                if (state.IsGenerating || !string.IsNullOrWhiteSpace(state.ErrorText))
                {
                    await SaveCheckpointAsync(monitor.Id, index, state.IsGenerating ? "Busy" : "ChatError", message, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var sent = await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken).ConfigureAwait(false);
                await _database.AddLogAsync("Outbound", message, string.Empty, sent ? "TaskPlanMessageSent" : "TaskPlanMessageFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken).ConfigureAwait(false);
                if (sent)
                {
                    var nextIndex = (index + 1) % catalog.Messages.Count;
                    await SaveCheckpointAsync(monitor.Id, index, "Sent", message, cancellationToken, nextIndex).ConfigureAwait(false);
                    sentCount++;
                    Activity?.Invoke($"[{monitor.Title}] plan '{planTitle}' message {index + 1}/{catalog.Messages.Count} delivered; next={nextIndex + 1}.");
                }
                else
                    await SaveCheckpointAsync(monitor.Id, index, "SendFailed", message, cancellationToken).ConfigureAwait(false);
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
            }
        }

        await _database.SetSettingAsync(LastCycleKey, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync(LastSentCountKey, sentCount.ToString(), cancellationToken).ConfigureAwait(false);
        await _database.SetSettingAsync("TaskAutomation.LastCatalogCount", catalog.Messages.Count.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsDevelopmentAutomationEnabledAsync(long monitorId, CancellationToken cancellationToken)
    {
        var value = await _database.GetSettingAsync($"TaskAutomation.Monitor.{monitorId}.Enabled", cancellationToken).ConfigureAwait(false);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetMonitorSettingAsync(long monitorId, string name, string defaultValue, CancellationToken cancellationToken)
    {
        var value = await _database.GetSettingAsync($"TaskAutomation.Monitor.{monitorId}.{name}", cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string BuildMessage(TaskMessageCatalog catalog, int index, string planId, string planTitle)
    {
        var template = catalog.Messages[index % catalog.Messages.Count];
        return template.Replace("{planId}", planId, StringComparison.OrdinalIgnoreCase)
            .Replace("{planTitle}", planTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{step}", (index + 1).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{total}", catalog.Messages.Count.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetMonitorMessageIndexAsync(long monitorId, int catalogCount, CancellationToken cancellationToken)
    {
        var raw = await _database.GetSettingAsync($"TaskAutomation.Monitor.{monitorId}.NextMessageIndex", cancellationToken).ConfigureAwait(false);
        return int.TryParse(raw, out var index) ? Math.Clamp(index, 0, Math.Max(0, catalogCount - 1)) : 0;
    }

    private async Task SaveCheckpointAsync(long monitorId, int messageIndex, string status, string message, CancellationToken cancellationToken, int? nextIndex = null)
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
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
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
        try { await StopAsync().ConfigureAwait(false); } catch { }
        _lifecycle.Dispose();
    }
}
