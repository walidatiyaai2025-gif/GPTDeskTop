using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

public enum GlobalChatTurnPhase
{
    Idle,
    AwaitingResponse,
    Cooldown
}

public sealed record GlobalChatTurnFenceSnapshot(
    long? ActiveMonitorId,
    GlobalChatTurnPhase Phase,
    DateTimeOffset? ActivatedUtc,
    DateTimeOffset? CooldownUntilUtc,
    string Reason);

/// <summary>
/// Process-wide turn fence for saved-monitor ChatGPT automation.
///
/// A physical send activates one monitor as the only runnable chat owner. Other
/// monitors may remain started, but they cannot enter a browser-operation cycle while
/// that turn is awaiting its authoritative response. Once the response is completed,
/// every monitor observes one mandatory 15-second quiet period before another turn can
/// enter. The current owner is still allowed to poll its own target so response
/// completion can be observed without deadlocking the global operation gate.
/// </summary>
public sealed class GlobalChatTurnFence
{
    public static GlobalChatTurnFence Shared { get; } = new();

    public static readonly TimeSpan DefaultPostResponseCooldown = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(200);
    private readonly object _sync = new();
    private long? _activeMonitorId;
    private DateTimeOffset? _activatedUtc;
    private DateTimeOffset? _cooldownUntilUtc;
    private string _reason = "idle";

    public long? ActiveMonitorId
    {
        get
        {
            lock (_sync)
            {
                RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
                return _activeMonitorId;
            }
        }
    }

    public DateTimeOffset? CooldownUntilUtc
    {
        get
        {
            lock (_sync)
            {
                RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
                return _cooldownUntilUtc;
            }
        }
    }

    public GlobalChatTurnFenceSnapshot Snapshot()
    {
        lock (_sync)
        {
            RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
            return new GlobalChatTurnFenceSnapshot(
                _activeMonitorId,
                _activeMonitorId is not null
                    ? GlobalChatTurnPhase.AwaitingResponse
                    : _cooldownUntilUtc is not null
                        ? GlobalChatTurnPhase.Cooldown
                        : GlobalChatTurnPhase.Idle,
                _activatedUtc,
                _cooldownUntilUtc,
                _reason);
        }
    }

    public bool CanRunMonitor(long monitorId)
    {
        lock (_sync)
        {
            RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
            if (_cooldownUntilUtc is not null)
                return false;
            return _activeMonitorId is null || _activeMonitorId == monitorId;
        }
    }

    public bool CanAttemptSend(long monitorId, out string reason)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            RefreshExpiredCooldownLocked(now);
            if (_cooldownUntilUtc is not null)
            {
                var remaining = _cooldownUntilUtc.Value - now;
                reason = $"post-response cooldown active for {Math.Max(0, Math.Ceiling(remaining.TotalSeconds)):0}s";
                return false;
            }

            if (_activeMonitorId is not null && _activeMonitorId != monitorId)
            {
                reason = $"monitor M{_activeMonitorId.Value} owns the active ChatGPT turn";
                return false;
            }

            reason = "runnable";
            return true;
        }
    }

    public async Task WaitUntilRunnableAsync(long monitorId, CancellationToken cancellationToken)
    {
        var announced = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan wait;
            string reason;
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                RefreshExpiredCooldownLocked(now);

                if (_cooldownUntilUtc is null
                    && (_activeMonitorId is null || _activeMonitorId == monitorId))
                {
                    if (announced)
                    {
                        RuntimeFlightRecorder.Record(
                            "ChatTurn",
                            "GlobalTurnRunnable",
                            "released",
                            "global-slot-available",
                            monitorId);
                    }
                    return;
                }

                if (_activeMonitorId is not null && _activeMonitorId != monitorId)
                {
                    reason = $"owner=M{_activeMonitorId.Value}; phase=awaiting-response";
                    wait = WaitPollInterval;
                }
                else
                {
                    var remaining = (_cooldownUntilUtc ?? now) - now;
                    reason = $"phase=post-response-cooldown; remainingMs={Math.Max(0, (long)Math.Ceiling(remaining.TotalMilliseconds))}";
                    wait = remaining <= TimeSpan.Zero
                        ? TimeSpan.FromMilliseconds(10)
                        : remaining < WaitPollInterval ? remaining : WaitPollInterval;
                }
            }

            if (!announced)
            {
                announced = true;
                RuntimeFlightRecorder.Record(
                    "ChatTurn",
                    "WaitingForGlobalSlot",
                    "waiting",
                    reason,
                    monitorId);
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Activate(long monitorId, string reason)
    {
        var recordedReason = string.IsNullOrWhiteSpace(reason) ? "physical-send-attempted" : reason.Trim();
        lock (_sync)
        {
            RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
            if (_activeMonitorId is not null && _activeMonitorId != monitorId)
            {
                RuntimeFlightRecorder.Record(
                    "ChatTurn",
                    "GlobalTurnOverlapViolation",
                    "blocked",
                    $"owner=M{_activeMonitorId.Value}; attempted=M{monitorId}; reason={recordedReason}",
                    monitorId);
                return;
            }

            _activeMonitorId = monitorId;
            _activatedUtc = DateTimeOffset.UtcNow;
            _cooldownUntilUtc = null;
            _reason = recordedReason;
        }

        RuntimeFlightRecorder.Record(
            "ChatTurn",
            "GlobalTurnActivated",
            "active",
            recordedReason,
            monitorId);
    }

    public bool Complete(long monitorId, string reason, TimeSpan? cooldown = null)
    {
        var quietPeriod = cooldown ?? DefaultPostResponseCooldown;
        if (quietPeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(cooldown), "Post-response cooldown cannot be negative.");

        DateTimeOffset until;
        var recordedReason = string.IsNullOrWhiteSpace(reason) ? "response-completed" : reason.Trim();
        lock (_sync)
        {
            RefreshExpiredCooldownLocked(DateTimeOffset.UtcNow);
            if (_activeMonitorId != monitorId)
                return false;

            _activeMonitorId = null;
            _activatedUtc = null;
            until = DateTimeOffset.UtcNow + quietPeriod;
            _cooldownUntilUtc = quietPeriod == TimeSpan.Zero ? null : until;
            _reason = recordedReason;
        }

        RuntimeFlightRecorder.Record(
            "ChatTurn",
            "GlobalTurnCompleted",
            quietPeriod == TimeSpan.Zero ? "released" : "cooldown",
            quietPeriod == TimeSpan.Zero
                ? recordedReason
                : $"{recordedReason}; cooldownSeconds={quietPeriod.TotalSeconds:0}",
            monitorId);
        return true;
    }

    private void RefreshExpiredCooldownLocked(DateTimeOffset now)
    {
        if (_cooldownUntilUtc is null || now < _cooldownUntilUtc.Value)
            return;

        _cooldownUntilUtc = null;
        _reason = "idle";
    }

    internal void ResetForTests()
    {
        lock (_sync)
        {
            _activeMonitorId = null;
            _activatedUtc = null;
            _cooldownUntilUtc = null;
            _reason = "idle";
        }
    }
}

public sealed class GlobalChatTurnYieldException : InvalidOperationException
{
    public GlobalChatTurnYieldException(string message) : base(message)
    {
    }
}
