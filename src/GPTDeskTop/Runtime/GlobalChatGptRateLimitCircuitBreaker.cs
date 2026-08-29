using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

public sealed record GlobalRateLimitStatus(
    bool IsActive,
    int BackoffStep,
    DateTimeOffset? RetryAtUtc,
    TimeSpan Remaining,
    string EventName,
    string Detail);

public sealed class GlobalChatGptRateLimitCircuitBreaker
{
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    public static GlobalChatGptRateLimitCircuitBreaker Shared { get; } = new();

    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private bool _active;
    private int _backoffIndex;
    private DateTimeOffset _retryAtUtc;

    public GlobalChatGptRateLimitCircuitBreaker(
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public event Action<GlobalRateLimitStatus>? StatusChanged;

    public bool IsActive
    {
        get { lock (_sync) return _active; }
    }

    public int BackoffStep
    {
        get { lock (_sync) return _active ? _backoffIndex + 1 : 0; }
    }

    public DateTimeOffset? RetryAtUtc
    {
        get { lock (_sync) return _active ? _retryAtUtc : null; }
    }

    public string DisplayStatus
    {
        get
        {
            lock (_sync)
            {
                if (!_active) return "READY";
                var remaining = _retryAtUtc - _utcNow();
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                return $"PAUSED {remaining:hh\\:mm\\:ss}";
            }
        }
    }

    public void ObserveVisibleState(string? visibleGlobalRateLimitText)
    {
        var limited = IsRateLimitText(visibleGlobalRateLimitText);
        GlobalRateLimitStatus? publish = null;
        string? secondEvent = null;

        lock (_sync)
        {
            var now = _utcNow();
            if (!_active)
            {
                if (!limited) return;
                _active = true;
                _backoffIndex = 0;
                _retryAtUtc = now + BackoffSchedule[_backoffIndex];
                publish = SnapshotLocked("RateLimitDetected", "visible-global-chatgpt-rate-limit");
                secondEvent = "GlobalSendPause";
            }
            else
            {
                if (now < _retryAtUtc)
                    return;

                RuntimeFlightRecorder.Record("RateLimit", "RateLimitProbe", "started", "single-global-cooldown-probe");
                if (limited)
                {
                    _backoffIndex = Math.Min(_backoffIndex + 1, BackoffSchedule.Length - 1);
                    _retryAtUtc = now + BackoffSchedule[_backoffIndex];
                    publish = SnapshotLocked("RateLimitStillActive", $"backoff={BackoffSchedule[_backoffIndex].TotalMinutes:0}m");
                }
                else
                {
                    _active = false;
                    _backoffIndex = 0;
                    _retryAtUtc = default;
                    publish = SnapshotLocked("RateLimitCleared", "visible-global-rate-limit-absent-at-probe");
                    secondEvent = "GlobalSendResume";
                }
            }
        }

        if (publish is not null)
        {
            RuntimeFlightRecorder.Record("RateLimit", publish.EventName, publish.IsActive ? "paused" : "ready", publish.Detail);
            Publish(publish);
        }
        if (secondEvent is not null)
            RuntimeFlightRecorder.Record("RateLimit", secondEvent, secondEvent == "GlobalSendPause" ? "paused" : "resumed", "global-send-authority");
    }

    public async Task WaitUntilAllowedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_sync)
            {
                if (!_active) return;
                wait = _retryAtUtc - _utcNow();
                if (wait <= TimeSpan.Zero) wait = TimeSpan.FromMilliseconds(250);
                if (wait > TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            }
            await _delayAsync(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool IsRateLimitText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("making requests too quickly", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("temporarily limited access", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("temporarily limited access to your conversations", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("please wait a few minutes before trying again", StringComparison.OrdinalIgnoreCase);
    }

    private GlobalRateLimitStatus SnapshotLocked(string eventName, string detail)
    {
        var remaining = _active ? _retryAtUtc - _utcNow() : TimeSpan.Zero;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return new GlobalRateLimitStatus(
            _active,
            _active ? _backoffIndex + 1 : 0,
            _active ? _retryAtUtc : null,
            remaining,
            eventName,
            detail);
    }

    private void Publish(GlobalRateLimitStatus status)
    {
        var handlers = StatusChanged;
        if (handlers is null) return;
        foreach (var subscriber in handlers.GetInvocationList())
        {
            try { ((Action<GlobalRateLimitStatus>)subscriber)(status); }
            catch (Exception ex) { ExceptionLogService.Log(ex, "GlobalChatGptRateLimitCircuitBreaker.StatusChanged"); }
        }
    }
}
