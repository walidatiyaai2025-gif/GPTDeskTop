using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

public enum OutboundDeliveryPhase
{
    Queued,
    Sending,
    Accepted,
    ReconcileRequired,
    Completed
}

public sealed record OutboundDeliverySnapshot(
    long MonitorId,
    string ConversationKey,
    string MessageFingerprint,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc,
    string Reason);

public sealed record OutboundDeliveryStatus(
    long MonitorId,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc);

public sealed record OutboundQueueStatus(
    int QueuedCount,
    long? ActiveMonitorId,
    DateTimeOffset UpdatedUtc);

public sealed class OutboundDeliveryCoordinator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultInterSendGap = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<long, OutboundDeliverySnapshot> _snapshots = new();
    private readonly object _queueSync = new();
    private readonly Queue<QueueEntry> _queue = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _interSendGap;
    private QueueEntry? _active;
    private long _sequence;

    public OutboundDeliveryCoordinator()
    : this(null, null)
{
}

public OutboundDeliveryCoordinator(
    Func<TimeSpan, CancellationToken, Task>? delayAsync,
    TimeSpan? interSendGap = null)
    {
        _delayAsync = delayAsync ?? Task.Delay;
        _interSendGap = interSendGap ?? DefaultInterSendGap;
    }

    public event Action<OutboundDeliveryStatus>? StatusChanged;
    public event Action<OutboundQueueStatus>? QueueStatusChanged;

    public int QueuedCount
    {
        get
        {
            lock (_queueSync)
                return _queue.Count(entry => !entry.Cancelled);
        }
    }

    public long? ActiveMonitorId
    {
        get { lock (_queueSync) return _active?.MonitorId; }
    }

    public string DisplayStatus
    {
        get
        {
            lock (_queueSync)
            {
                var queued = _queue.Count(entry => !entry.Cancelled);
                return _active is null
                    ? queued == 0 ? "IDLE" : $"WAITING {queued}"
                    : queued == 0 ? $"M{_active.MonitorId} SENDING" : $"M{_active.MonitorId} +{queued}";
            }
        }
    }

    public async Task<bool> SendOnceAsync(
        long monitorId,
        string conversationKey,
        string message,
        Func<Task<bool>> physicalSend,
        Action<string>? activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(physicalSend);
        using var flightScope = RuntimeFlightRecorder.BeginScope(monitorId, conversationKey);
        RuntimeFlightRecorder.Record("Delivery", "OperationRequested", reason: "global-serialized-logical-send");

        var fingerprint = Fingerprint(message);
        var logicalId = $"{monitorId}:{conversationKey}:{fingerprint}";
        using var queueLease = await AcquireQueueLeaseAsync(monitorId, logicalId, cancellationToken).ConfigureAwait(false);
        var physicalSendAttempted = false;
        try
        {
            await GlobalChatGptRateLimitCircuitBreaker.Shared.WaitUntilAllowedAsync(cancellationToken).ConfigureAwait(false);

            if (_snapshots.TryGetValue(monitorId, out var previous)
                && IsDuplicateInFlight(previous, conversationKey, fingerprint))
            {
                RuntimeFlightRecorder.Record("Delivery", "DuplicateSuppressed", "suppressed", "uncertain-or-in-flight");
                activity?.Invoke("Exactly-once guard: identical delivery is already in-flight or awaiting reconciliation; duplicate composer mutation suppressed.");
                return false;
            }

            var sending = new OutboundDeliverySnapshot(
                monitorId,
                conversationKey,
                fingerprint,
                OutboundDeliveryPhase.Sending,
                1,
                DateTimeOffset.UtcNow,
                "global-queue-authority-persisted-before-physical-send");
            SetSnapshot(sending);
            RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitRequested", "started", "global-queue-authority");

            bool accepted;
            try
            {
                physicalSendAttempted = true;
                accepted = await physicalSend().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetSnapshot(sending with
                {
                    Phase = OutboundDeliveryPhase.ReconcileRequired,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Reason = "physical-send-threw; observe-before-any-future-send"
                });
                RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitCompleted", "uncertain", ex.GetType().Name);
                throw;
            }

            SetSnapshot(sending with
            {
                Phase = accepted ? OutboundDeliveryPhase.Accepted : OutboundDeliveryPhase.ReconcileRequired,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Reason = accepted
                    ? "verified-user-message-receipt"
                    : "receipt-not-confirmed; no blind retry"
            });
            RuntimeFlightRecorder.Record(
                "Delivery",
                "PhysicalSubmitCompleted",
                accepted ? "confirmed" : "uncertain",
                accepted ? "verified-user-turn" : "receipt-not-confirmed");
            return accepted;
        }
        finally
        {
            if (physicalSendAttempted)
            {
                activity?.Invoke($"Global send queue: enforcing {_interSendGap.TotalSeconds:0}-second inter-send cooldown.");
                RuntimeFlightRecorder.Record("Delivery", "GlobalInterSendCooldown", "started", $"{_interSendGap.TotalSeconds:0}s", monitorId);
                try
                {
                    await _delayAsync(_interSendGap, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.InterSendCooldown");
                }
            }
        }
    }

    public IReadOnlyList<OutboundDeliverySnapshot> Snapshot()
        => _snapshots.Values.OrderBy(x => x.MonitorId).ToArray();

    public void MarkCompleted(long monitorId)
    {
        if (!_snapshots.TryGetValue(monitorId, out var state)
            || state.Phase is not (OutboundDeliveryPhase.Accepted or OutboundDeliveryPhase.ReconcileRequired))
            return;

        var reason = state.Phase == OutboundDeliveryPhase.ReconcileRequired
            ? "response-observed-after-uncertain-send"
            : "response-observed";
        SetSnapshot(state with
        {
            Phase = OutboundDeliveryPhase.Completed,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Reason = reason
        });
        RuntimeFlightRecorder.Record("Delivery", "OperationCompleted", "completed", reason, monitorId);
    }

    public void CancelMonitor(long monitorId)
    {
        lock (_queueSync)
        {
            foreach (var entry in _queue.Where(entry => entry.MonitorId == monitorId))
                entry.Cancel();
            PruneCancelledHeadLocked();
            PublishQueueStatusLocked();
        }
    }

    private async Task<QueueLease> AcquireQueueLeaseAsync(long monitorId, string logicalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QueueEntry entry;
        lock (_queueSync)
        {
            entry = new QueueEntry(++_sequence, monitorId, logicalId, cancellationToken);
            _queue.Enqueue(entry);
            entry.CancellationRegistration = cancellationToken.Register(() => CancelQueuedEntry(entry));
            RuntimeFlightRecorder.Record("Delivery", "GlobalQueueEnqueued", "queued", $"sequence={entry.Sequence}", monitorId);
            GrantNextLocked();
            PublishQueueStatusLocked();
        }

        try
        {
            return await entry.Completion.Task.ConfigureAwait(false);
        }
        catch
        {
            entry.CancellationRegistration.Dispose();
            throw;
        }
    }

    private void CancelQueuedEntry(QueueEntry entry)
    {
        lock (_queueSync)
        {
            if (ReferenceEquals(_active, entry))
                return;
            entry.Cancel();
            PruneCancelledHeadLocked();
            GrantNextLocked();
            PublishQueueStatusLocked();
        }
    }

    private void Release(QueueEntry entry)
    {
        lock (_queueSync)
        {
            if (!ReferenceEquals(_active, entry))
                return;
            _active = null;
            entry.CancellationRegistration.Dispose();
            RuntimeFlightRecorder.Record("Delivery", "GlobalQueueReleased", "released", $"sequence={entry.Sequence}", entry.MonitorId);
            PruneCancelledHeadLocked();
            GrantNextLocked();
            PublishQueueStatusLocked();
        }
    }

    private void GrantNextLocked()
    {
        if (_active is not null)
            return;

        PruneCancelledHeadLocked();
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (next.Cancelled)
                continue;
            _active = next;
            var lease = new QueueLease(this, next);
            if (next.Completion.TrySetResult(lease))
            {
                RuntimeFlightRecorder.Record("Delivery", "GlobalQueueAuthorityGranted", "granted", $"sequence={next.Sequence}", next.MonitorId);
                break;
            }
            _active = null;
        }
    }

    private void PruneCancelledHeadLocked()
    {
        while (_queue.Count > 0 && _queue.Peek().Cancelled)
            _queue.Dequeue();
    }

    private void PublishQueueStatusLocked()
    {
        var handlers = QueueStatusChanged;
        if (handlers is null)
            return;
        var status = new OutboundQueueStatus(
            _queue.Count(entry => !entry.Cancelled),
            _active?.MonitorId,
            DateTimeOffset.UtcNow);
        // Dashboard/diagnostic observers are never allowed to alter exactly-once delivery.
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<OutboundQueueStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.QueueStatusChanged"); }
        }
    }

    private void SetSnapshot(OutboundDeliverySnapshot snapshot)
    {
        _snapshots[snapshot.MonitorId] = snapshot;
        PublishStatus(new OutboundDeliveryStatus(
            snapshot.MonitorId,
            snapshot.Phase,
            snapshot.PhysicalSendCount,
            snapshot.UpdatedUtc));
    }

    private void PublishStatus(OutboundDeliveryStatus status)
    {
        var handlers = StatusChanged;
        if (handlers is null)
            return;
        // Dashboard/diagnostic observers are never allowed to alter exactly-once delivery.
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<OutboundDeliveryStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.StatusChanged"); }
        }
    }

    private static bool IsDuplicateInFlight(OutboundDeliverySnapshot previous, string conversationKey, string fingerprint)
        => previous.ConversationKey == conversationKey
           && previous.MessageFingerprint == fingerprint
           && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired
           && DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow;

    public static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private sealed class QueueEntry
    {
        public QueueEntry(long sequence, long monitorId, string logicalId, CancellationToken cancellationToken)
        {
            Sequence = sequence;
            MonitorId = monitorId;
            LogicalId = logicalId;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<QueueLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public long Sequence { get; }
        public long MonitorId { get; }
        public string LogicalId { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<QueueLease> Completion { get; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool Cancelled { get; private set; }

        public void Cancel()
        {
            if (Cancelled) return;
            Cancelled = true;
            Completion.TrySetCanceled(CancellationToken);
        }
    }

    private sealed class QueueLease : IDisposable
    {
        private readonly OutboundDeliveryCoordinator _owner;
        private readonly QueueEntry _entry;
        private int _disposed;

        public QueueLease(OutboundDeliveryCoordinator owner, QueueEntry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.Release(_entry);
        }
    }
}

