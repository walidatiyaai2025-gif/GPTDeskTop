using GPTDeskTop.Models;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services;

public sealed record GenerationLeaseSnapshot(
    long MonitorId,
    string ConversationIdentity,
    string TargetId,
    bool IsGenerationActive,
    DateTimeOffset GenerationObservedAtUtc,
    DateTimeOffset LastAuthoritativeGenerationStateUtc,
    int ConsecutiveNonGeneratingObservations);

/// <summary>
/// Process-wide recovery authority for monitored ChatGPT generations. An active lease never
/// expires with time. Only two fresh authoritative non-generating observations release it.
/// </summary>
public sealed class GenerationRecoveryInterlock
{
    public static GenerationRecoveryInterlock Shared { get; } = new();

    private readonly object _sync = new();
    private readonly Dictionary<long, GenerationLeaseSnapshot> _leases = new();

    public GenerationLeaseSnapshot Observe(long monitorId, ChromeTab tab, bool isGenerating, DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var now = observedAtUtc ?? DateTimeOffset.UtcNow;
        GenerationLeaseSnapshot next;
        var acquired = false;
        var released = false;
        lock (_sync)
        {
            _leases.TryGetValue(monitorId, out var current);
            if (isGenerating)
            {
                acquired = current is null || !current.IsGenerationActive;
                next = new GenerationLeaseSnapshot(
                    monitorId, tab.Url, tab.Id, true, now, now, 0);
            }
            else if (current?.IsGenerationActive == true)
            {
                var confirmations = current.ConsecutiveNonGeneratingObservations + 1;
                released = confirmations >= 2;
                next = current with
                {
                    ConversationIdentity = tab.Url,
                    TargetId = tab.Id,
                    IsGenerationActive = !released,
                    LastAuthoritativeGenerationStateUtc = now,
                    ConsecutiveNonGeneratingObservations = confirmations
                };
            }
            else
            {
                next = new GenerationLeaseSnapshot(monitorId, tab.Url, tab.Id, false, now, now, 0);
            }
            _leases[monitorId] = next;
        }

        if (acquired)
            RuntimeFlightRecorder.Record("Monitor", "GenerationLeaseAcquired", "active", "authoritative-generating", monitorId, tab.Id, tab.Url);
        if (released)
            RuntimeFlightRecorder.Record("Monitor", "GenerationLeaseReleased", "released", "two-authoritative-non-generating-observations", monitorId, tab.Id, tab.Url);
        return next;
    }

    public bool IsActive(long monitorId)
    {
        lock (_sync) return _leases.TryGetValue(monitorId, out var lease) && lease.IsGenerationActive;
    }

    public bool IsActive(ChromeTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        lock (_sync)
            return _leases.Values.Any(lease => lease.IsGenerationActive
                && (string.Equals(lease.TargetId, tab.Id, StringComparison.Ordinal)
                    || ChatGptConversationIdentity.IsSame(lease.ConversationIdentity, tab.Url)));
    }

    public bool HasAnyActiveLease
    {
        get { lock (_sync) return _leases.Values.Any(lease => lease.IsGenerationActive); }
    }

    public GenerationLeaseSnapshot? Snapshot(long monitorId)
    {
        lock (_sync) return _leases.TryGetValue(monitorId, out var lease) ? lease : null;
    }

    public bool ReleaseMonitor(long monitorId, string reason)
    {
        GenerationLeaseSnapshot? removed;
        lock (_sync)
        {
            if (!_leases.Remove(monitorId, out removed))
            {
                // A verified physical send can activate the global turn fence before the next
                // authoritative generating observation exists. Stopping that monitor must still
                // release the process-wide turn instead of leaving every other monitor parked.
                return GlobalChatTurnFence.Shared.Complete(
                    monitorId,
                    $"monitor worker ended before generation lease materialized: {reason}");
            }
        }

        RuntimeFlightRecorder.Record(
            "Monitor",
            "GenerationLeaseReleased",
            removed.IsGenerationActive ? "released" : "cleared",
            reason,
            monitorId,
            removed.TargetId,
            removed.ConversationIdentity);
        GlobalChatTurnFence.Shared.Complete(
            monitorId,
            $"monitor worker released generation ownership: {reason}");
        return true;
    }

    public void RecordSuppressed(long monitorId, ChromeTab tab, string operation)
        => RuntimeFlightRecorder.Record("Monitor", "RecoverySuppressed", "suppressed", $"active-generation:{operation}", monitorId, tab.Id, tab.Url);

    public bool ConfirmTargetDestroyed(ChromeTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        List<long> released = [];
        lock (_sync)
        {
            foreach (var pair in _leases.ToArray())
            {
                if (!pair.Value.IsGenerationActive || !string.Equals(pair.Value.TargetId, tab.Id, StringComparison.Ordinal))
                    continue;
                _leases[pair.Key] = pair.Value with
                {
                    IsGenerationActive = false,
                    LastAuthoritativeGenerationStateUtc = DateTimeOffset.UtcNow
                };
                released.Add(pair.Key);
            }
        }
        foreach (var monitorId in released)
        {
            RuntimeFlightRecorder.Record("Monitor", "GenerationLeaseReleased", "target-destroyed", "target-positively-confirmed-missing", monitorId, tab.Id, tab.Url);
            GlobalChatTurnFence.Shared.Complete(
                monitorId,
                "generation target positively confirmed destroyed");
        }
        return released.Count > 0;
    }

    internal void ResetForTests()
    {
        lock (_sync) _leases.Clear();
    }
}
