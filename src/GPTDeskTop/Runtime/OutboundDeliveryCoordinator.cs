using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

internal enum OutboundDeliveryPhase
{
    Queued,
    Sending,
    Accepted,
    ReconcileRequired,
    Completed
}

internal sealed record OutboundDeliverySnapshot(
    long MonitorId,
    string ConversationKey,
    string MessageFingerprint,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc,
    string Reason);

/// <summary>
/// Privacy-safe delivery state for operator UI. Conversation keys, tab keys, message text and
/// fingerprints are intentionally excluded.
/// </summary>
internal sealed record OutboundDeliveryStatus(
    long MonitorId,
    OutboundDeliveryPhase Phase,
    int PhysicalSendCount,
    DateTimeOffset UpdatedUtc);

internal sealed class OutboundDeliveryCoordinator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan GlobalSendCooldown = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan GlobalLeaseHardCeiling = TimeSpan.FromMinutes(20);

    private readonly ConcurrentDictionary<long, SemaphoreSlim> _monitorGates = new();
    private readonly ConcurrentDictionary<long, OutboundDeliverySnapshot> _snapshots = new();
    private readonly SemaphoreSlim _globalGate = new(1, 1);
    private readonly object _globalSync = new();
    private long? _globalOwnerMonitorId;
    private long _globalLeaseGeneration;

    internal event Action<OutboundDeliveryStatus>? StatusChanged;

    public async Task<bool> SendOnceAsync(
        long monitorId,
        string conversationKey,
        string message,
        Func<Task<bool>> physicalSend,
        Action<string>? activity,
        CancellationToken cancellationToken)
    {
        using var flightScope = RuntimeFlightRecorder.BeginScope(monitorId, conversationKey);
        RuntimeFlightRecorder.Record("Delivery", "OperationRequested", reason: "logical-send");

        var fingerprint = Fingerprint(message);
        var monitorGate = _monitorGates.GetOrAdd(monitorId, _ => new SemaphoreSlim(1, 1));
        await monitorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshots.TryGetValue(monitorId, out var previous)
                && IsDuplicateInFlight(previous, conversationKey, fingerprint))
            {
                RuntimeFlightRecorder.Record("Delivery", "DuplicateSuppressed", "suppressed", "uncertain-or-in-flight");
                activity?.Invoke("Exactly-once guard: identical delivery is already in-flight or awaiting reconciliation; duplicate composer mutation suppressed.");
                return false;
            }

            if (!await _globalGate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                activity?.Invoke("WAITING_FOR_GLOBAL_SLOT: another ChatGPT monitor owns the outbound execution slot. No composer mutation will occur until it completes and the cooldown expires.");
                RuntimeFlightRecorder.Record("Delivery", "GlobalSlotWait", "waiting", "another-monitor-active");
                await _globalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var leaseGeneration = ClaimGlobalLease(monitorId);
            var releaseGlobalOnExit = true;
            try
            {
                var sending = new OutboundDeliverySnapshot(
                    monitorId,
                    conversationKey,
                    fingerprint,
                    OutboundDeliveryPhase.Sending,
                    1,
                    DateTimeOffset.UtcNow,
                    "persisted-before-physical-send");
                SetSnapshot(sending);
                RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitRequested", "started", "persisted-before-send");

                bool accepted;
                try
                {
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
                    releaseGlobalOnExit = false;
                    ScheduleHardCeilingRelease(monitorId, leaseGeneration, activity);
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

                // The global slot intentionally remains owned after the physical send. It is released
                // only after the monitor observes the terminal assistant response (MarkCompleted),
                // followed by the mandatory cooldown. This prevents another monitor from sending while
                // the current ChatGPT turn is still generating.
                releaseGlobalOnExit = false;
                ScheduleHardCeilingRelease(monitorId, leaseGeneration, activity);
                return accepted;
            }
            finally
            {
                if (releaseGlobalOnExit)
                    ReleaseGlobalLeaseIfOwned(monitorId, leaseGeneration, "send-exited-before-active-delivery");
            }
        }
        finally
        {
            monitorGate.Release();
        }
    }

    public IReadOnlyList<OutboundDeliverySnapshot> Snapshot() => _snapshots.Values.OrderBy(x => x.MonitorId).ToArray();

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

        var leaseGeneration = GetOwnedLeaseGeneration(monitorId);
        if (leaseGeneration is null)
            return;

        _ = ReleaseAfterCooldownAsync(monitorId, leaseGeneration.Value);
    }

    /// <summary>
    /// Releases an owned global slot immediately when a monitor is explicitly stopped or its worker
    /// terminates. This prevents a cancelled monitor from orphaning the process-wide send gate.
    /// </summary>
    public void AbandonMonitor(long monitorId, string reason = "monitor-stopped")
    {
        var leaseGeneration = GetOwnedLeaseGeneration(monitorId);
        if (leaseGeneration is null)
            return;

        ReleaseGlobalLeaseIfOwned(monitorId, leaseGeneration.Value, reason);
    }

    private long ClaimGlobalLease(long monitorId)
    {
        lock (_globalSync)
        {
            if (_globalOwnerMonitorId is not null)
                throw new InvalidOperationException("Global outbound gate was acquired while another owner was still recorded.");

            _globalOwnerMonitorId = monitorId;
            return ++_globalLeaseGeneration;
        }
    }

    private long? GetOwnedLeaseGeneration(long monitorId)
    {
        lock (_globalSync)
            return _globalOwnerMonitorId == monitorId ? _globalLeaseGeneration : null;
    }

    private async Task ReleaseAfterCooldownAsync(long monitorId, long leaseGeneration)
    {
        RuntimeFlightRecorder.Record("Delivery", "GlobalCooldown", "started", $"{GlobalSendCooldown.TotalSeconds:0}s", monitorId);
        await Task.Delay(GlobalSendCooldown).ConfigureAwait(false);
        ReleaseGlobalLeaseIfOwned(monitorId, leaseGeneration, "response-complete-cooldown-expired");
    }

    private void ScheduleHardCeilingRelease(long monitorId, long leaseGeneration, Action<string>? activity)
    {
        _ = ReleaseAtHardCeilingAsync(monitorId, leaseGeneration, activity);
    }

    private async Task ReleaseAtHardCeilingAsync(long monitorId, long leaseGeneration, Action<string>? activity)
    {
        await Task.Delay(GlobalLeaseHardCeiling).ConfigureAwait(false);
        if (!ReleaseGlobalLeaseIfOwned(monitorId, leaseGeneration, "hard-ceiling-expired"))
            return;

        activity?.Invoke($"STALLED: no terminal response was observed for {GlobalLeaseHardCeiling.TotalMinutes:0} minutes. The global send slot was released so other monitors are not blocked indefinitely; this monitor still will not mutate a generating composer.");
        RuntimeFlightRecorder.Record("Delivery", "GlobalSlotHardCeiling", "released", "stalled-owner", monitorId);
    }

    private bool ReleaseGlobalLeaseIfOwned(long monitorId, long leaseGeneration, string reason)
    {
        lock (_globalSync)
        {
            if (_globalOwnerMonitorId != monitorId || _globalLeaseGeneration != leaseGeneration)
                return false;

            _globalOwnerMonitorId = null;
            _globalGate.Release();
            RuntimeFlightRecorder.Record("Delivery", "GlobalSlotReleased", "released", reason, monitorId);
            return true;
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

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<OutboundDeliveryStatus>)subscriber)(status);
            }
            catch (Exception ex)
            {
                // Dashboard/diagnostic observers are never allowed to alter exactly-once delivery.
                ExceptionLogService.Log(ex, "OutboundDeliveryCoordinator.StatusChanged");
            }
        }
    }

    private static bool IsDuplicateInFlight(OutboundDeliverySnapshot previous, string conversationKey, string fingerprint)
        => previous.ConversationKey == conversationKey
           && previous.MessageFingerprint == fingerprint
           && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired
           && DateTimeOffset.UtcNow - previous.UpdatedUtc < DuplicateWindow;

    internal static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
