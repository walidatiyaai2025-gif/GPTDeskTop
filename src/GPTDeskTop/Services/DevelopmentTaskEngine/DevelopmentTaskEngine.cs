using System.Text.Json;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

public sealed class DevelopmentTaskEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _stateFileGate = new(1, 1);
    private readonly string _statePath;
    private readonly string _messagesPath;
    private readonly DevelopmentTaskScheduleSettingsStore _scheduleStore;
    private readonly bool _workWindowOverridden;
    private readonly bool _coolingWindowOverridden;
    private TimeSpan _workWindow;
    private TimeSpan _coolingWindow;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private DevelopmentTaskState _state = new();
    private int? _lastEmittedMessageIndex;
    private bool _messageDeliveredThisWindow;
    private int _disposeState;

    public DevelopmentTaskEngine(TimeSpan? workWindow = null, TimeSpan? coolingWindow = null, string? statePath = null, string? messagesPath = null, string? scheduleSettingsPath = null)
    {
        _statePath = statePath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-state.json");
        _messagesPath = messagesPath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-messages.json");
        _scheduleStore = new DevelopmentTaskScheduleSettingsStore(scheduleSettingsPath);
        _workWindowOverridden = workWindow.HasValue;
        _coolingWindowOverridden = coolingWindow.HasValue;
        var configured = _scheduleStore.Load();
        _workWindow = workWindow ?? TimeSpan.FromMinutes(configured.WorkMinutes);
        _coolingWindow = coolingWindow ?? TimeSpan.FromMinutes(configured.CoolingMinutes);
    }

    public DevelopmentTaskState State => _state;
    public TimeSpan WorkWindow => _workWindow;
    public TimeSpan CoolingWindow => _coolingWindow;
    public event EventHandler<DevelopmentTaskState>? StateChanged;
    public event Action<string>? MessageReady;
    public event EventHandler? CoolingStarted;
    public event EventHandler? CoolingCompleted;

    public async Task StartAsync(string planId, string planTitle, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReloadScheduleSettings();
            await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0) throw new InvalidOperationException("No development task messages are configured.");
            _state.PlanId = planId;
            _state.PlanTitle = planTitle;
            _state.TotalMessages = messages.Count;
            _state.Status = DevelopmentTaskEngineStatus.Working;
            _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
            _state.CoolingStartedAt = null;
            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            _messageDeliveredThisWindow = false;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            await RestartWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.Status is not (DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling)) return;
            await StopWorkerAsync().ConfigureAwait(false);
            _state.Status = DevelopmentTaskEngineStatus.Paused;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopWorkerAsync().ConfigureAwait(false);
            _state.Status = DevelopmentTaskEngineStatus.Stopped;
            _state.WorkWindowStartedAt = null;
            _state.CoolingStartedAt = null;
            _lastEmittedMessageIndex = null;
            _messageDeliveredThisWindow = false;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReloadScheduleSettings();
            await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            if (_state.Status == DevelopmentTaskEngineStatus.Completed) return;
            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0) throw new InvalidOperationException("No development task messages are configured.");
            _state.TotalMessages = messages.Count;
            if (_state.Status is DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Paused)
            {
                _state.Status = DevelopmentTaskEngineStatus.Working;
                _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
                _state.CoolingStartedAt = null;
                _messageDeliveredThisWindow = false;
            }
            else if (_state.Status == DevelopmentTaskEngineStatus.Working)
            {
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = _state.LastDeliveredMessageIndex == _state.CurrentMessageIndex - 1 && !string.IsNullOrWhiteSpace(_state.LastDeliveredMessageFingerprint);
            }
            else
            {
                _messageDeliveredThisWindow = false;
            }
            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            await RestartWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void RestorePosition(int messageIndex, int completedMessages, DevelopmentTaskEngineStatus status)
    {
        if (messageIndex < 0) throw new ArgumentOutOfRangeException(nameof(messageIndex));
        if (completedMessages < 0) throw new ArgumentOutOfRangeException(nameof(completedMessages));
        _state.CurrentMessageIndex = messageIndex;
        _state.CompletedMessages = completedMessages;
        _state.Status = status;
        _lastEmittedMessageIndex = null;
        _messageDeliveredThisWindow = status == DevelopmentTaskEngineStatus.Working &&
            _state.LastDeliveredMessageIndex == messageIndex - 1 &&
            !string.IsNullOrWhiteSpace(_state.LastDeliveredMessageFingerprint);
    }

    public async Task CheckpointAsync(string? monitorId, string? tabId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.LastMonitorId = monitorId;
            _state.LastTabId = tabId;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    public async Task CheckpointDeliveredAsync(string monitorId, string tabId, string fingerprint, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.LastMonitorId = monitorId;
            _state.LastTabId = tabId;
            _state.LastDeliveredMessageIndex = _state.CurrentMessageIndex;
            _state.LastDeliveredMessageFingerprint = fingerprint;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            _messageDeliveredThisWindow = true;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (_state.CurrentMessageIndex < messages.Count) _state.CurrentMessageIndex++;
            _state.CompletedMessages = _state.CurrentMessageIndex;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            _lastEmittedMessageIndex = null;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    private void ReloadScheduleSettings()
    {
        var settings = _scheduleStore.Load();
        if (!_workWindowOverridden) _workWindow = TimeSpan.FromMinutes(settings.WorkMinutes);
        if (!_coolingWindowOverridden) _coolingWindow = TimeSpan.FromMinutes(settings.CoolingMinutes);
    }

    private async Task RestartWorkerAsync(CancellationToken cancellationToken)
    {
        await StopWorkerAsync().ConfigureAwait(false);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workerTask = RunLoopAsync(_cts.Token);
    }

    private async Task StopWorkerAsync()
    {
        var cts = _cts;
        var worker = _workerTask;
        _cts = null;
        _workerTask = null;

        if (cts is null && worker is null) return;

        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { }

        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        cts?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
                if (messages.Count == 0) return;
                _state.TotalMessages = messages.Count;
                if (_state.CurrentMessageIndex >= messages.Count)
                {
                    _state.Status = DevelopmentTaskEngineStatus.Completed;
                    await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                    PublishState();
                    return;
                }
                if (_state.Status == DevelopmentTaskEngineStatus.Working)
                {
                    var started = _state.WorkWindowStartedAt ?? DateTimeOffset.UtcNow;
                    _state.WorkWindowStartedAt = started;
                    var remaining = _workWindow - (DateTimeOffset.UtcNow - started);
                    if (remaining <= TimeSpan.Zero)
                    {
                        _state.Status = DevelopmentTaskEngineStatus.Cooling;
                        _state.CoolingStartedAt ??= DateTimeOffset.UtcNow;
                        _lastEmittedMessageIndex = null;
                        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                        PublishState();
                        CoolingStarted?.Invoke(this, EventArgs.Empty);
                        continue;
                    }
                    if (!_messageDeliveredThisWindow && _lastEmittedMessageIndex != _state.CurrentMessageIndex)
                    {
                        var message = BuildPlanMessage(messages[_state.CurrentMessageIndex], _state);
                        _lastEmittedMessageIndex = _state.CurrentMessageIndex;
                        MessageReady?.Invoke(message);
                    }
                    var delay = _messageDeliveredThisWindow
                        ? TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(100, remaining.TotalMilliseconds)))
                        : TimeSpan.FromMilliseconds(250);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (_state.Status == DevelopmentTaskEngineStatus.Cooling)
                {
                    await RunCoolingAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (_state.Status is DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Faulted) return;
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _state.Status = DevelopmentTaskEngineStatus.Faulted;
            _state.LastError = ex.Message;
            await SaveStateAsync(CancellationToken.None).ConfigureAwait(false);
            PublishState();
        }
    }

    private async Task RunCoolingAsync(CancellationToken cancellationToken)
    {
        var started = _state.CoolingStartedAt ?? DateTimeOffset.UtcNow;
        _state.CoolingStartedAt = started;
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        PublishState();
        var remaining = _coolingWindow - (DateTimeOffset.UtcNow - started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        ReloadScheduleSettings();
        _state.Status = DevelopmentTaskEngineStatus.Working;
        _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
        _state.CoolingStartedAt = null;
        _lastEmittedMessageIndex = null;
        _messageDeliveredThisWindow = false;
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        PublishState();
        CoolingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static string BuildPlanMessage(string template, DevelopmentTaskState state) => template
        .Replace("{planId}", state.PlanId, StringComparison.OrdinalIgnoreCase)
        .Replace("{planTitle}", state.PlanTitle, StringComparison.OrdinalIgnoreCase)
        .Replace("{step}", (state.CurrentMessageIndex + 1).ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{total}", state.TotalMessages.ToString(), StringComparison.OrdinalIgnoreCase);

    private async Task<List<string>> LoadMessagesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_messagesPath)) return [];
        await using var stream = File.OpenRead(_messagesPath);
        var document = await JsonSerializer.DeserializeAsync<MessageDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken).ConfigureAwait(false);
        return document?.Messages?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
    }

    private async Task LoadStateAsync(CancellationToken cancellationToken)
    {
        await _stateFileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_statePath)) return;
            await using var stream = File.OpenRead(_statePath);
            _state = await JsonSerializer.DeserializeAsync<DevelopmentTaskState>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new DevelopmentTaskState();
        }
        finally { _stateFileGate.Release(); }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        await _stateFileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = _statePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, _state, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _statePath, true);
        }
        finally { _stateFileGate.Release(); }
    }

    private void PublishState() => StateChanged?.Invoke(this, _state);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        await StopWorkerAsync().ConfigureAwait(false);
        _gate.Dispose();
        _stateFileGate.Dispose();
    }

    private sealed class MessageDocument { public List<string> Messages { get; set; } = []; }
}