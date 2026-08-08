using System.Text.Json;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

public sealed class DevelopmentTaskEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _stateFileGate = new(1, 1);
    private readonly string _statePath;
    private readonly string _messagesPath;
    private readonly TimeSpan _workWindow;
    private readonly TimeSpan _coolingWindow;
    private CancellationTokenSource? _cts;
    private DevelopmentTaskState _state = new();
    private int? _lastEmittedMessageIndex;

    public DevelopmentTaskEngine(TimeSpan? workWindow = null, TimeSpan? coolingWindow = null, string? statePath = null, string? messagesPath = null)
    {
        _workWindow = workWindow ?? TimeSpan.FromMinutes(10);
        _coolingWindow = coolingWindow ?? TimeSpan.FromMinutes(5);
        _statePath = statePath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-state.json");
        _messagesPath = messagesPath ?? Path.Combine(AppContext.BaseDirectory, "data", "development-task-messages.json");
    }

    public DevelopmentTaskState State => _state;
    public event EventHandler<DevelopmentTaskState>? StateChanged;
    public event EventHandler<string>? MessageReady;
    public event EventHandler? CoolingStarted;
    public event EventHandler? CoolingCompleted;

    public async Task StartAsync(string planId, string planTitle, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            RestartWorker(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cts?.Cancel();
            _state.Status = DevelopmentTaskEngineStatus.Stopped;
            _state.WorkWindowStartedAt = null;
            _state.CoolingStartedAt = null;
            _lastEmittedMessageIndex = null;
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
            }
            else if (_state.Status == DevelopmentTaskEngineStatus.Working && _state.WorkWindowStartedAt is null)
            {
                _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
            }
            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            RestartWorker(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Restores a checkpointed position without resetting delivery identity.</summary>
    public void RestorePosition(int messageIndex, int completedMessages, DevelopmentTaskEngineStatus status)
    {
        if (messageIndex < 0) throw new ArgumentOutOfRangeException(nameof(messageIndex));
        if (completedMessages < 0) throw new ArgumentOutOfRangeException(nameof(completedMessages));
        _state.CurrentMessageIndex = messageIndex;
        _state.CompletedMessages = completedMessages;
        _state.Status = status;
        _lastEmittedMessageIndex = null;
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

    private void RestartWorker(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunLoopAsync(_cts.Token);
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
                    if (_lastEmittedMessageIndex != _state.CurrentMessageIndex)
                    {
                        var message = BuildPlanMessage(messages[_state.CurrentMessageIndex], _state);
                        _lastEmittedMessageIndex = _state.CurrentMessageIndex;
                        MessageReady?.Invoke(this, message);
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
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
        _state.Status = DevelopmentTaskEngineStatus.Working;
        _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
        _state.CoolingStartedAt = null;
        _lastEmittedMessageIndex = null;
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
        var document = await JsonSerializer.DeserializeAsync<MessageDocument>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
        _stateFileGate.Dispose();
        await Task.CompletedTask;
    }

    private sealed class MessageDocument { public List<string> Messages { get; set; } = []; }
}
