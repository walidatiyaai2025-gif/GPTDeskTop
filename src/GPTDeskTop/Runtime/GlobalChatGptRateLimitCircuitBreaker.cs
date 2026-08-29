using System.Globalization;
using GPTDeskTop.Data;
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
    private const int DurableSchemaVersion = 1;
    private const string SettingsPrefix = "Runtime.GlobalRateLimit.";
    public const string SchemaVersionKey = SettingsPrefix + "SchemaVersion";
    public const string IsActiveKey = SettingsPrefix + "IsActive";
    public const string DetectedAtUtcKey = SettingsPrefix + "DetectedAtUtc";
    public const string BackoffIndexKey = SettingsPrefix + "BackoffIndex";
    public const string RetryAtUtcKey = SettingsPrefix + "RetryAtUtc";
    public const string LastReasonKey = SettingsPrefix + "LastReason";
    public const string LastCategoryKey = SettingsPrefix + "LastCategory";
    public const string LastTransitionKey = SettingsPrefix + "LastTransition";
    public const string UpdatedAtUtcKey = SettingsPrefix + "UpdatedAtUtc";

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
    private LocalDatabase? _database;
    private bool _active;
    private int _backoffIndex;
    private DateTimeOffset _retryAtUtc;
    private DateTimeOffset? _detectedAtUtc;
    private string _lastReason = string.Empty;
    private string _lastCategory = string.Empty;
    private string _lastTransition = string.Empty;

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

    public DateTimeOffset? DetectedAtUtc
    {
        get { lock (_sync) return _detectedAtUtc; }
    }

    public string LastReason
    {
        get { lock (_sync) return _lastReason; }
    }

    public string LastCategory
    {
        get { lock (_sync) return _lastCategory; }
    }

    public string LastTransition
    {
        get { lock (_sync) return _lastTransition; }
    }

    public bool IsProbeEligible
    {
        get { lock (_sync) return _active && _utcNow() >= _retryAtUtc; }
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

    public async Task InitializeAsync(LocalDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var activeRaw = await database.GetSettingAsync(IsActiveKey, cancellationToken).ConfigureAwait(false);
        var detectedRaw = await database.GetSettingAsync(DetectedAtUtcKey, cancellationToken).ConfigureAwait(false);
        var backoffRaw = await database.GetSettingAsync(BackoffIndexKey, cancellationToken).ConfigureAwait(false);
        var retryRaw = await database.GetSettingAsync(RetryAtUtcKey, cancellationToken).ConfigureAwait(false);
        var reason = await database.GetSettingAsync(LastReasonKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var category = await database.GetSettingAsync(LastCategoryKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var transition = await database.GetSettingAsync(LastTransitionKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;

        var active = string.Equals(activeRaw, "1", StringComparison.Ordinal);
        var now = _utcNow();
        var detectedAt = ParseTimestamp(detectedRaw);
        var backoffIndex = int.TryParse(backoffRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
            ? Math.Clamp(parsedIndex, 0, BackoffSchedule.Length - 1)
            : 0;
        var retryAt = ParseTimestamp(retryRaw);
        var repaired = false;

        if (active)
        {
            if (!detectedAt.HasValue)
            {
                detectedAt = now;
                repaired = true;
            }
            if (!retryAt.HasValue)
            {
                retryAt = now + BackoffSchedule[backoffIndex];
                repaired = true;
            }
        }

        DurableState restored;
        lock (_sync)
        {
            _database = database;
            restored = new DurableState(
                active,
                detectedAt,
                active ? backoffIndex : 0,
                active ? retryAt : null,
                reason,
                category,
                transition);
            ApplyLocked(restored);
        }

        if (active && repaired)
            await PersistAsync(restored, cancellationToken).ConfigureAwait(false);

        if (active)
        {
            var status = Snapshot("RateLimitRestored", retryAt > now
                ? "persisted-global-breaker-restored-before-retry-deadline"
                : "persisted-global-breaker-restored-probe-eligible");
            RuntimeFlightRecorder.Record("RateLimit", "RateLimitRestored", "paused", status.Detail);
            RuntimeFlightRecorder.Record("RateLimit", "GlobalSendPause", "paused", "restored-global-send-authority-fence");
            Publish(status);
        }
    }

    public void ObserveVisibleState(string? visibleGlobalRateLimitText)
    {
        var limited = IsRateLimitText(visibleGlobalRateLimitText);
        var classification = Classify(visibleGlobalRateLimitText);
        GlobalRateLimitStatus? publish = null;
        string? secondEvent = null;

        lock (_sync)
        {
            var now = _utcNow();
            if (!_active)
            {
                if (!limited) return;

                var target = new DurableState(
                    true,
                    now,
                    0,
                    now + BackoffSchedule[0],
                    classification.Reason,
                    classification.Category,
                    "RateLimitDetected|RetryScheduled");
                PersistTargetLocked(target);
                ApplyLocked(target);
                publish = SnapshotLocked("RateLimitDetected", $"category={classification.Category}; retry={_retryAtUtc:O}; backoff=5m");
                secondEvent = "GlobalSendPause";
            }
            else
            {
                if (now < _retryAtUtc)
                    return;

                RuntimeFlightRecorder.Record("RateLimit", "RateLimitProbe", "started", "single-global-cooldown-probe");
                if (limited)
                {
                    var nextIndex = Math.Min(_backoffIndex + 1, BackoffSchedule.Length - 1);
                    var transition = nextIndex == _backoffIndex
                        ? "RateLimitStillActive|RetryScheduled"
                        : "RateLimitStillActive|BackoffAdvanced|RetryScheduled";
                    var target = new DurableState(
                        true,
                        _detectedAtUtc ?? now,
                        nextIndex,
                        now + BackoffSchedule[nextIndex],
                        classification.Reason,
                        classification.Category,
                        transition);
                    PersistTargetLocked(target);
                    ApplyLocked(target);
                    publish = SnapshotLocked("RateLimitStillActive", $"category={classification.Category}; backoff={BackoffSchedule[_backoffIndex].TotalMinutes:0}m");
                }
                else
                {
                    var target = new DurableState(
                        false,
                        _detectedAtUtc,
                        0,
                        null,
                        _lastReason,
                        _lastCategory,
                        "RateLimitCleared");
                    // Persist the authorization transition before clearing the in-memory fence.
                    // A storage failure therefore leaves this process blocked instead of fail-open.
                    PersistTargetLocked(target);
                    ApplyLocked(target);
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

    private void PersistTargetLocked(DurableState state)
    {
        var database = _database;
        if (database is null)
            return;
        PersistAsync(database, state, CancellationToken.None).GetAwaiter().GetResult();
    }

    private Task PersistAsync(DurableState state, CancellationToken cancellationToken)
    {
        LocalDatabase? database;
        lock (_sync) database = _database;
        return database is null
            ? Task.CompletedTask
            : PersistAsync(database, state, cancellationToken);
    }

    private static Task PersistAsync(LocalDatabase database, DurableState state, CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        return database.SetSettingsAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaVersionKey] = DurableSchemaVersion.ToString(CultureInfo.InvariantCulture),
            [IsActiveKey] = state.IsActive ? "1" : "0",
            [DetectedAtUtcKey] = state.DetectedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            [BackoffIndexKey] = state.BackoffIndex.ToString(CultureInfo.InvariantCulture),
            [RetryAtUtcKey] = state.RetryAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            [LastReasonKey] = state.LastReason,
            [LastCategoryKey] = state.LastCategory,
            [LastTransitionKey] = state.LastTransition,
            [UpdatedAtUtcKey] = updatedAt.ToString("O", CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    private void ApplyLocked(DurableState state)
    {
        _active = state.IsActive;
        _detectedAtUtc = state.DetectedAtUtc;
        _backoffIndex = state.IsActive ? Math.Clamp(state.BackoffIndex, 0, BackoffSchedule.Length - 1) : 0;
        _retryAtUtc = state.IsActive && state.RetryAtUtc.HasValue ? state.RetryAtUtc.Value : default;
        _lastReason = state.LastReason;
        _lastCategory = state.LastCategory;
        _lastTransition = state.LastTransition;
    }

    private GlobalRateLimitStatus Snapshot(string eventName, string detail)
    {
        lock (_sync) return SnapshotLocked(eventName, detail);
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

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static RateLimitClassification Classify(string? text)
    {
        var normalized = text ?? string.Empty;
        if (normalized.Contains("too many requests", StringComparison.OrdinalIgnoreCase))
            return new("visible-global-chatgpt-rate-limit", "too_many_requests");
        if (normalized.Contains("making requests too quickly", StringComparison.OrdinalIgnoreCase))
            return new("visible-global-chatgpt-rate-limit", "requests_too_quickly");
        if (normalized.Contains("temporarily limited access", StringComparison.OrdinalIgnoreCase))
            return new("visible-global-chatgpt-rate-limit", "temporarily_limited_access");
        if (normalized.Contains("please wait a few minutes", StringComparison.OrdinalIgnoreCase))
            return new("visible-global-chatgpt-rate-limit", "wait_before_retry");
        return new("visible-global-chatgpt-rate-limit", "rate_limit");
    }

    private sealed record DurableState(
        bool IsActive,
        DateTimeOffset? DetectedAtUtc,
        int BackoffIndex,
        DateTimeOffset? RetryAtUtc,
        string LastReason,
        string LastCategory,
        string LastTransition);

    private sealed record RateLimitClassification(string Reason, string Category);
}
