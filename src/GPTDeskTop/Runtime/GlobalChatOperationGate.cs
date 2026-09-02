using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

/// <summary>
/// Process-wide, fail-closed authority for automated ChatGPT browser operations.
/// Saved monitors must acquire this lease before touching a ChatGPT target so
/// recovery, navigation, polling and delivery cannot race across conversations.
/// A saved monitor that owns an active ChatGPT response turn may keep polling its
/// own target, while every other monitor waits outside the browser-operation gate.
/// </summary>
public sealed class GlobalChatOperationGate
{
    public static GlobalChatOperationGate Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly GlobalChatTurnFence _turnFence;
    private long? _activeMonitorId;
    private int _queuedCount;
    private long _sequence;

    public GlobalChatOperationGate()
        : this(GlobalChatTurnFence.Shared)
    {
    }

    public GlobalChatOperationGate(GlobalChatTurnFence turnFence)
    {
        _turnFence = turnFence ?? throw new ArgumentNullException(nameof(turnFence));
    }

    public long? ActiveMonitorId
    {
        get { lock (_sync) return _activeMonitorId; }
    }

    public int QueuedCount
    {
        get { lock (_sync) return _queuedCount; }
    }

    public async Task<Lease> AcquireAsync(
        long monitorId,
        string operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        operation = string.IsNullOrWhiteSpace(operation) ? "chat-operation" : operation.Trim();

        long sequence;
        lock (_sync)
        {
            sequence = ++_sequence;
            _queuedCount++;
        }

        RuntimeFlightRecorder.Record(
            "ChatOperation",
            "GlobalOperationQueued",
            "queued",
            $"sequence={sequence}; operation={operation}",
            monitorId);

        try
        {
            while (true)
            {
                // Crucially this wait happens before the semaphore is held. A monitor whose
                // response is still generating therefore remains able to reacquire the gate
                // and observe completion; queued monitors cannot deadlock the response owner.
                await _turnFence.WaitUntilRunnableAsync(monitorId, cancellationToken).ConfigureAwait(false);
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Close the race where another monitor starts a physical turn between the
                // pre-gate fence observation and this semaphore acquisition.
                if (_turnFence.CanRunMonitor(monitorId))
                    break;

                _gate.Release();
            }
        }
        catch
        {
            lock (_sync) _queuedCount = Math.Max(0, _queuedCount - 1);
            throw;
        }

        lock (_sync)
        {
            _queuedCount = Math.Max(0, _queuedCount - 1);
            _activeMonitorId = monitorId;
        }

        RuntimeFlightRecorder.Record(
            "ChatOperation",
            "GlobalOperationAcquired",
            "active",
            $"sequence={sequence}; operation={operation}",
            monitorId);

        return new Lease(this, monitorId, sequence, operation);
    }

    private void Release(long monitorId, long sequence, string operation)
    {
        lock (_sync)
        {
            if (_activeMonitorId == monitorId)
                _activeMonitorId = null;
        }

        RuntimeFlightRecorder.Record(
            "ChatOperation",
            "GlobalOperationReleased",
            "released",
            $"sequence={sequence}; operation={operation}",
            monitorId);

        _gate.Release();
    }

    public sealed class Lease : IDisposable
    {
        private readonly GlobalChatOperationGate _owner;
        private readonly long _monitorId;
        private readonly long _sequence;
        private readonly string _operation;
        private int _disposed;

        internal Lease(GlobalChatOperationGate owner, long monitorId, long sequence, string operation)
        {
            _owner = owner;
            _monitorId = monitorId;
            _sequence = sequence;
            _operation = operation;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.Release(_monitorId, _sequence, _operation);
        }
    }
}
