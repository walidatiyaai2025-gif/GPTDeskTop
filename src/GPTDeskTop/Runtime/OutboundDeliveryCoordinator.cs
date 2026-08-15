using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

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
        var gate = _gates.GetOrAdd(monitorId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fingerprint = Fingerprint(message);
            var now = DateTimeOffset.UtcNow;
            if (_snapshots.TryGetValue(monitorId, out var previous)
                && previous.ConversationKey == conversationKey
                && previous.MessageFingerprint == fingerprint
                && previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired
                && now - previous.UpdatedUtc < TimeSpan.FromMinutes(2))
            {
                activity?.Invoke("Exactly-once guard: identical delivery is already uncertain/in-flight; composer mutation suppressed while state is reconciled.");
                return false;
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

            bool accepted;
            try
            {
                accepted = await physicalSend().ConfigureAwait(false);
            }
            catch
            {
                _snapshots[monitorId] = sending with
                {
                    Phase = OutboundDeliveryPhase.ReconcileRequired,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Reason = "physical-send-threw; observe-before-any-future-send"
                };
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
            _snapshots[monitorId] = state with { Phase = OutboundDeliveryPhase.Completed, UpdatedUtc = DateTimeOffset.UtcNow, Reason = "response-observed" };
    }

    internal static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
