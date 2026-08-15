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
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<long, OutboundDeliverySnapshot> _snapshots = new();

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
        var gate = _gates.GetOrAdd(monitorId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            gate.Release();
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
