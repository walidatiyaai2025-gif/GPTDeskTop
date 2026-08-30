using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Starts a new explicit plan run. Crash/process recovery uses ResumeIfActiveAsync instead,
    /// so an operator pressing Start always gets prompt #1 rather than inheriting a completed run.
    /// </summary>
    public async Task StartAsync(string planId, string planTitle, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReloadScheduleSettings();
            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0) throw new InvalidOperationException("No development task messages are configured.");

            await StopWorkerAsync().ConfigureAwait(false);
            _state = new DevelopmentTaskState
            {
                PlanId = planId,
                PlanTitle = planTitle,
                TotalMessages = messages.Count,
                Status = DevelopmentTaskEngineStatus.Working,
                WorkWindowStartedAt = DateTimeOffset.UtcNow,
                LastCheckpointAt = DateTimeOffset.UtcNow,
                Revision = _state.Revision + 1
            };
            _lastEmittedMessageIndex = null;
            _messageDeliveredThisWindow = false;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            StartWorker(cancellationToken);
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
            _messageDeliveredThisWindow = _state.AwaitingAssistantResponse;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
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
            NormalizeLoadedState(messages.Count);

            if (_state.CurrentMessageIndex >= messages.Count)
            {
                _state.Status = DevelopmentTaskEngineStatus.Completed;
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                PublishState();
                return;
            }

            if (_state.AwaitingAssistantResponse)
            {
                _state.Status = DevelopmentTaskEngineStatus.Working;
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = true;
            }
            else if (_state.Status is DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Faulted)
            {
                _state.Status = DevelopmentTaskEngineStatus.Working;
                _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
                _state.CoolingStartedAt = null;
                _messageDeliveredThisWindow = false;
            }
            else if (_state.Status == DevelopmentTaskEngineStatus.Working)
            {
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = HasCurrentMessageDeliveryReceipt();
            }
            else
            {
                _state.CoolingStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = false;
            }

            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            await RestartWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReloadScheduleSettings();
            await LoadStateAsync(cancellationToken).ConfigureAwait(false);
            if (_state.Status is not (DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling))
            {
                PublishState();
                return false;
            }

            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0) throw new InvalidOperationException("No development task messages are configured.");
            NormalizeLoadedState(messages.Count);
            if (_state.CurrentMessageIndex >= messages.Count)
            {
                _state.Status = DevelopmentTaskEngineStatus.Completed;
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                PublishState();
                return false;
            }

            if (_state.AwaitingAssistantResponse)
            {
                // A verified outbound already exists. Never re-emit it after restart; wait for
                // the monitor's stable ResponseReceived event to close the exact message step.
                _state.Status = DevelopmentTaskEngineStatus.Working;
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _state.CoolingStartedAt = null;
                _messageDeliveredThisWindow = true;
            }
            else if (_state.Status == DevelopmentTaskEngineStatus.Working)
            {
                _state.WorkWindowStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = HasCurrentMessageDeliveryReceipt();
            }
            else
            {
                _state.CoolingStartedAt ??= DateTimeOffset.UtcNow;
                _messageDeliveredThisWindow = false;
            }

            _state.LastError = null;
            _lastEmittedMessageIndex = null;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            await RestartWorkerAsync(cancellationToken).ConfigureAwait(false);
            return true;
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
        _messageDeliveredThisWindow = _state.AwaitingAssistantResponse || HasCurrentMessageDeliveryReceipt();
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

    /// <summary>
    /// Closes only the outbound half of a plan step. The message index intentionally does not
    /// move here; completion belongs to HandleAssistantResponseAsync after the monitor proves a
    /// stable non-generating assistant response.
    /// </summary>
    public async Task MarkAwaitingAssistantResponseAsync(
        IEnumerable<string> monitorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitorIds);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var expected = monitorIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (expected.Count == 0)
                throw new InvalidOperationException("A development-plan message cannot wait for a response without an eligible monitor recipient.");

            _state.AwaitingAssistantResponse = true;
            _state.AwaitingResponseMessageIndex = _state.CurrentMessageIndex;
            _state.AwaitingResponseSince = DateTimeOffset.UtcNow;
            _state.AwaitingResponseMonitorIds = expected;
            _state.CompletedResponseMonitorIds = [];
            _state.LastError = null;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            _messageDeliveredThisWindow = true;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Called only from the canonical monitor stable-response event. Generating/extended-thinking
    /// UI never reaches this method. Multiple monitor recipients must all complete before the plan
    /// advances, and duplicate response events are idempotent.
    /// </summary>
    public async Task<bool> HandleAssistantResponseAsync(
        string monitorId,
        string response,
        bool isError,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(monitorId)) return false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.AwaitingAssistantResponse ||
                _state.AwaitingResponseMessageIndex != _state.CurrentMessageIndex ||
                !_state.AwaitingResponseMonitorIds.Contains(monitorId, StringComparer.Ordinal))
                return false;

            if (isError)
            {
                _state.LastError = $"Monitor {monitorId} reported a ChatGPT error while waiting for the assistant response. Recovery remains active; the plan position was not advanced.";
                _state.LastCheckpointAt = DateTimeOffset.UtcNow;
                _state.Revision++;
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                PublishState();
                return false;
            }

            if (!_state.CompletedResponseMonitorIds.Contains(monitorId, StringComparer.Ordinal))
                _state.CompletedResponseMonitorIds.Add(monitorId);

            _state.LastAssistantResponseAt = DateTimeOffset.UtcNow;
            _state.LastAssistantResponseFingerprint = Fingerprint(response ?? string.Empty);
            _state.LastError = null;
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;

            var allComplete = _state.AwaitingResponseMonitorIds.All(
                id => _state.CompletedResponseMonitorIds.Contains(id, StringComparer.Ordinal));
            if (!allComplete)
            {
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                PublishState();
                return false;
            }

            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            CompleteCurrentMessage(messages.Count);
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task ReportDeliveryFailureAsync(string error, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.LastError = string.IsNullOrWhiteSpace(error) ? "Development message delivery failed." : error.Trim();
            _state.LastCheckpointAt = DateTimeOffset.UtcNow;
            _state.Revision++;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Explicit/manual advancement retained for recovery tools and tests. Verified delivery paths
    /// must use MarkAwaitingAssistantResponseAsync + HandleAssistantResponseAsync instead.
    /// </summary>
    public async Task AdvanceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            CompleteCurrentMessage(messages.Count, enterCooling: false);
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            PublishState();
        }
        finally { _gate.Release(); }
    }

    private void CompleteCurrentMessage(int totalMessages, bool enterCooling = true)
    {
        if (_state.CurrentMessageIndex < totalMessages) _state.CurrentMessageIndex++;
        _state.CompletedMessages = _state.CurrentMessageIndex;
        _state.AwaitingAssistantResponse = false;
        _state.AwaitingResponseMessageIndex = -1;
        _state.AwaitingResponseSince = null;
        _state.AwaitingResponseMonitorIds = [];
        _state.CompletedResponseMonitorIds = [];
        _state.LastCheckpointAt = DateTimeOffset.UtcNow;
        _state.Revision++;
        _lastEmittedMessageIndex = null;
        _messageDeliveredThisWindow = false;

        if (_state.CurrentMessageIndex >= totalMessages)
        {
            _state.Status = DevelopmentTaskEngineStatus.Completed;
            _state.WorkWindowStartedAt = null;
            _state.CoolingStartedAt = null;
            return;
        }

        if (enterCooling && _coolingWindow > TimeSpan.Zero)
        {
            _state.Status = DevelopmentTaskEngineStatus.Cooling;
            _state.WorkWindowStartedAt = null;
            _state.CoolingStartedAt = DateTimeOffset.UtcNow;
            CoolingStarted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _state.Status = DevelopmentTaskEngineStatus.Working;
            _state.WorkWindowStartedAt = DateTimeOffset.UtcNow;
            _state.CoolingStartedAt = null;
        }
    }

    private void NormalizeLoadedState(int totalMessages)
    {
        _state.TotalMessages = totalMessages;
        _state.DeliveryReceipts ??= new Dictionary<string, DevelopmentTaskDeliveryReceipt>(StringComparer.Ordinal);
        _state.AwaitingResponseMonitorIds ??= [];
        _state.CompletedResponseMonitorIds ??= [];
        _state.CurrentMessageIndex = Math.Clamp(_state.CurrentMessageIndex, 0, totalMessages);
        _state.CompletedMessages = Math.Clamp(_state.CompletedMessages, 0, _state.CurrentMessageIndex);
        if (_state.AwaitingAssistantResponse && _state.AwaitingResponseMessageIndex < 0)
            _state.AwaitingResponseMessageIndex = _state.CurrentMessageIndex;
    }

    private bool HasCurrentMessageDeliveryReceipt()
        => _state.DeliveryReceipts.Values.Any(receipt => receipt.MessageIndex == _state.CurrentMessageIndex);

    private void ReloadScheduleSettings()
    {
        var settings = _scheduleStore.Load();
        if (!_workWindowOverridden) _workWindow = TimeSpan.FromMinutes(settings.WorkMinutes);
        if (!_coolingWindowOverridden) _coolingWindow = TimeSpan.FromMinutes(settings.CoolingMinutes);
    }

    private async Task RestartWorkerAsync(CancellationToken cancellationToken)
    {
        await StopWorkerAsync().ConfigureAwait(false);
        StartWorker(cancellationToken);
    }

    private void StartWorker(CancellationToken cancellationToken)
    {
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
                    // A verified prompt can take arbitrarily long to answer. Work-window expiration
                    // never rotates/cools while ChatGPT is generating or extended-thinking because
                    // the monitor has not emitted a stable response event yet.
                    if (_state.AwaitingAssistantResponse)
                    {
                        _messageDeliveredThisWindow = true;
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var started = _state.WorkWindowStartedAt ?? DateTimeOffset.UtcNow;
                    _state.WorkWindowStartedAt = started;
                    var remaining = _workWindow - (DateTimeOffset.UtcNow - started);
                    if (remaining <= TimeSpan.Zero)
                    {
                        _state.Status = DevelopmentTaskEngineStatus.Cooling;
                        _state.CoolingStartedAt ??= DateTimeOffset.UtcNow;
                        _lastEmittedMessageIndex = null;
                        _messageDeliveredThisWindow = false;
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

                if (_state.Status is DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Faulted)
                    return;

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
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(_messagesPath)) return [];
                await using var stream = new FileStream(
                    _messagesPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                var document = await JsonSerializer.DeserializeAsync<MessageDocument>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    cancellationToken).ConfigureAwait(false);
                return document?.Messages?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
            }
            catch (Exception ex) when ((ex is IOException || ex is JsonException) && attempt < 4)
            {
                await Task.Delay(25 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        return [];
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

    private static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
