using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GPTDeskTop.Runtime;

internal enum OutboundDeliveryPhase
{
    Queued,
    Sending,
    DomAccepted,
    AwaitingResponse,
    ResponseDetected,
    Completed,
    ReconcileRequired,
    Failed
}

internal sealed record OutboundDeliveryState(
    int MonitorId,
    string MessageId,
    string Fingerprint,
    OutboundDeliveryPhase Phase,
    DateTimeOffset UpdatedUtc,
    int SendAttempts,
    string? ConversationIdentity,
    string? LastReason)
{
    public bool MayMutateComposer => Phase is OutboundDeliveryPhase.Queued or OutboundDeliveryPhase.Failed;
    public bool IsAccepted => Phase >= OutboundDeliveryPhase.DomAccepted && Phase != OutboundDeliveryPhase.Failed;
}

internal static class OutboundMessageIdentity
{
    public static string Fingerprint(string text)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string CreateMessageId(int monitorId, string fingerprint) => $"m{monitorId}-{fingerprint[..Math.Min(16, fingerprint.Length)]}";
}

internal sealed class OutboundDeliveryJournal
{
    private readonly string _path;
    private readonly object _gate = new();

    public OutboundDeliveryJournal(string path) => _path = path;

    public OutboundDeliveryState? Load(int monitorId)
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            var all = JsonSerializer.Deserialize<List<OutboundDeliveryState>>(File.ReadAllText(_path)) ?? [];
            return all.LastOrDefault(x => x.MonitorId == monitorId && x.Phase != OutboundDeliveryPhase.Completed);
        }
    }

    public void Save(OutboundDeliveryState state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var all = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<OutboundDeliveryState>>(File.ReadAllText(_path)) ?? []
                : [];
            all.RemoveAll(x => x.MonitorId == state.MonitorId && x.MessageId == state.MessageId);
            all.Add(state);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

internal sealed class MonitorSendSingleFlight
{
    private readonly Dictionary<int, SemaphoreSlim> _locks = new();
    private readonly object _gate = new();

    public async ValueTask<IAsyncDisposable> EnterAsync(int monitorId, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore;
        lock (_gate)
        {
            if (!_locks.TryGetValue(monitorId, out semaphore!))
                _locks[monitorId] = semaphore = new SemaphoreSlim(1, 1);
        }
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; }
    }
}

internal static class OutboundDeliveryPolicy
{
    public static OutboundDeliveryState BeforeComposerMutation(int monitorId, string text, string? conversationIdentity, OutboundDeliveryState? pending)
    {
        var fingerprint = OutboundMessageIdentity.Fingerprint(text);
        if (pending is { IsAccepted: true } && pending.Fingerprint == fingerprint)
            return pending with { Phase = OutboundDeliveryPhase.ReconcileRequired, UpdatedUtc = DateTimeOffset.UtcNow, LastReason = "accepted-message-must-not-be-resent" };

        return new OutboundDeliveryState(monitorId, OutboundMessageIdentity.CreateMessageId(monitorId, fingerprint), fingerprint,
            OutboundDeliveryPhase.Sending, DateTimeOffset.UtcNow, (pending?.SendAttempts ?? 0) + 1, conversationIdentity, "persist-before-dom-mutation");
    }

    public static OutboundDeliveryState DomAccepted(OutboundDeliveryState state) =>
        state with { Phase = OutboundDeliveryPhase.DomAccepted, UpdatedUtc = DateTimeOffset.UtcNow, LastReason = "matching-user-message-observed" };

    public static OutboundDeliveryState ReceiptTimeout(OutboundDeliveryState state) =>
        state.IsAccepted
            ? state with { Phase = OutboundDeliveryPhase.ReconcileRequired, UpdatedUtc = DateTimeOffset.UtcNow, LastReason = "receipt-timeout-observe-do-not-resend" }
            : state with { Phase = OutboundDeliveryPhase.ReconcileRequired, UpdatedUtc = DateTimeOffset.UtcNow, LastReason = "delivery-unknown-reconcile-before-retry" };
}
