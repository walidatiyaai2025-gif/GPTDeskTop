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

internal sealed class OutboundDeliveryCoordinator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<long, OutboundDeliverySnapshot> _snapshots = new();

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

        var gate = _gates.GetOrAdd(monitorId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fingerprint = Fingerprint(message);
            var now = DateTimeOffset.UtcNow;
            if (_snapshots.TryGetValue(monitorId, out var previous)
                && IsSameRecentLogicalOperation(previous, conversationKey, fingerprint, now))
            {
                if (previous.Phase == OutboundDeliveryPhase.Sending)
                {
                    RuntimeFlightRecorder.Record("Delivery", "DuplicateSuppressed", "suppressed", "physical-send-in-flight");
                    activity?.Invoke("Exactly-once guard: identical delivery is still physically in-flight; duplicate composer mutation suppressed.");
                    return false;
                }

                if (previous.Phase == OutboundDeliveryPhase.ReconcileRequired)
                {
                    // ChatGptMonitorService advances lastHandledText before it requests an auto reply.
                    // Consequently, a later same-monitor/same-conversation logical request can only be
                    // reached after another stable assistant response has been observed. That response
                    // is the read-only reconciliation evidence the previous uncertain physical submit
                    // was accepted. Complete the old operation before starting the new continuation.
                    _snapshots[monitorId] = previous with
                    {
                        Phase = OutboundDeliveryPhase.Completed,
                        UpdatedUtc = now,
                        Reason = "response-observed-before-next-logical-send"
                    };
                    RuntimeFlightRecorder.Record(
                        "Delivery",
                        "OperationCompleted",
                        "completed",
                        "response-observed-before-next-logical-send");
                    activity?.Invoke("Exactly-once reconciliation: a new stable assistant response resolved the previous uncertain delivery; continuing with the next logical send.");
                }
            }

            var sending = new OutboundDeliverySnapshot(
                monitorId,
                conversationKey,
                fingerprint,
                OutboundDeliveryPhase.Sending,
                1,
                now,
                "persisted-before-physical-send");
            _snapshots[monitorId] = sending;
            RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitRequested", "started", "persisted-before-send");

            bool accepted;
            try
            {
                accepted = await physicalSend().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _snapshots[monitorId] = sending with
                {
                    Phase = OutboundDeliveryPhase.ReconcileRequired,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Reason = "physical-send-threw; observe-before-any-future-send"
                };
                RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitCompleted", "uncertain", ex.GetType().Name);
                throw;
            }

            _snapshots[monitorId] = sending with
            {
                Phase = accepted ? OutboundDeliveryPhase.Accepted : OutboundDeliveryPhase.ReconcileRequired,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Reason = accepted
                    ? "verified-user-message-receipt"
                    : "receipt-not-confirmed; no blind retry"
            };
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
        if (_snapshots.TryGetValue(monitorId, out var state))
        {
            _snapshots[monitorId] = state with { Phase = OutboundDeliveryPhase.Completed, UpdatedUtc = DateTimeOffset.UtcNow, Reason = "response-observed" };
            RuntimeFlightRecorder.Record("Delivery", "OperationCompleted", "completed", "response-observed", monitorId);
        }
    }

    private static bool IsSameRecentLogicalOperation(
        OutboundDeliverySnapshot previous,
        string conversationKey,
        string fingerprint,
        DateTimeOffset now)
        => previous.ConversationKey == conversationKey
           && previous.MessageFingerprint == fingerprint
           && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired
           && now - previous.UpdatedUtc >= TimeSpan.Zero
           && now - previous.UpdatedUtc < DuplicateWindow;

    internal static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
