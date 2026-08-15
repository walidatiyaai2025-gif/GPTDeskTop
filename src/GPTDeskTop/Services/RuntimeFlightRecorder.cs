using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GPTDeskTop.Services;

internal sealed record RuntimeFlightEvent(
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    string Category,
    string Action,
    string Outcome,
    string Reason,
    long? MonitorId,
    string? TabKey,
    string? ConversationKey);

internal sealed record RuntimeFlightSnapshot(
    int Capacity,
    int EventCount,
    long FirstSequence,
    long LastSequence,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyDictionary<long, int> MonitorCounts,
    IReadOnlyList<RuntimeFlightEvent> Events);

internal static partial class RuntimeFlightRecorder
{
    internal const int Capacity = 1000;

    private static readonly object Sync = new();
    private static readonly RuntimeFlightEvent?[] Buffer = new RuntimeFlightEvent?[Capacity];
    private static readonly AsyncLocal<FlightContext?> CurrentContext = new();
    private static long _sequence;
    private static int _nextIndex;
    private static int _count;

    private sealed record FlightContext(long? MonitorId, string? TabKey, string? ConversationKey);

    internal static IDisposable BeginScope(long? monitorId = null, string? tabId = null, string? conversationRef = null)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = new FlightContext(
            monitorId ?? previous?.MonitorId,
            HashOpaque(tabId) ?? previous?.TabKey,
            HashConversation(conversationRef) ?? previous?.ConversationKey);
        return new Scope(previous);
    }

    internal static void Record(
        string category,
        string action,
        string outcome = "observed",
        string reason = "none",
        long? monitorId = null,
        string? tabId = null,
        string? conversationRef = null)
    {
        var context = CurrentContext.Value;
        var sequence = Interlocked.Increment(ref _sequence);
        var item = new RuntimeFlightEvent(
            sequence,
            DateTimeOffset.UtcNow,
            SafeToken(category, "unknown"),
            SafeToken(action, "unknown"),
            SafeToken(outcome, "observed"),
            SafeToken(reason, "none"),
            monitorId ?? context?.MonitorId,
            HashOpaque(tabId) ?? context?.TabKey,
            HashConversation(conversationRef) ?? context?.ConversationKey);

        lock (Sync)
        {
            Buffer[_nextIndex] = item;
            _nextIndex = (_nextIndex + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    internal static RuntimeFlightSnapshot Snapshot()
    {
        RuntimeFlightEvent[] events;
        lock (Sync)
        {
            events = new RuntimeFlightEvent[_count];
            var start = (_nextIndex - _count + Capacity) % Capacity;
            for (var i = 0; i < _count; i++)
                events[i] = Buffer[(start + i) % Capacity]!;
        }

        var categoryCounts = events
            .GroupBy(item => item.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var monitorCounts = events
            .Where(item => item.MonitorId.HasValue)
            .GroupBy(item => item.MonitorId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        return new RuntimeFlightSnapshot(
            Capacity,
            events.Length,
            events.Length == 0 ? 0 : events[0].Sequence,
            events.Length == 0 ? 0 : events[^1].Sequence,
            categoryCounts,
            monitorCounts,
            events);
    }

    internal static string SafeIdentity(string? value)
        => SafeToken(value, "unnamed");

    private static string SafeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        if (trimmed.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("bearer", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("localstorage", StringComparison.OrdinalIgnoreCase))
            return "redacted";

        var safe = SafeTokenRegex().Replace(trimmed, "_");
        safe = RepeatedUnderscoreRegex().Replace(safe, "_").Trim('_');
        if (safe.Length == 0) return fallback;
        return safe.Length <= 96 ? safe : safe[..96];
    }

    private static string? HashConversation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var cIndex = Array.FindIndex(segments, segment => string.Equals(segment, "c", StringComparison.OrdinalIgnoreCase));
            candidate = cIndex >= 0 && cIndex + 1 < segments.Length ? segments[cIndex + 1] : uri.AbsolutePath;
        }
        return HashOpaque(candidate, "conv");
    }

    private static string? HashOpaque(string? value, string prefix = "tab")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return $"{prefix}:{Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            Array.Clear(Buffer);
            _nextIndex = 0;
            _count = 0;
            Interlocked.Exchange(ref _sequence, 0);
        }
        CurrentContext.Value = null;
    }

    private sealed class Scope(FlightContext? previous) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CurrentContext.Value = previous;
        }
    }

    [GeneratedRegex("[^A-Za-z0-9._:-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenRegex();

    [GeneratedRegex("_+")]
    private static partial Regex RepeatedUnderscoreRegex();
}
