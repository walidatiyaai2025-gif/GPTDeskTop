using System.Reflection;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.Services;

internal sealed record MonitorRuntimeDiagnostic(
    long MonitorId,
    string LifecycleStatus,
    string RawTaskStatus,
    bool IsCompleted,
    bool IsFaulted,
    bool CancellationRequested,
    DateTimeOffset ObservedSinceUtc,
    double ObservedForSeconds);

/// <summary>
/// Converts the monitor service's long-lived async worker into an operator-facing lifecycle state.
/// A healthy async Task commonly reports WaitingForActivation while awaiting I/O/timers; that raw
/// TaskStatus is retained only as a secondary diagnostic and must not be presented as monitor state.
/// </summary>
internal static class MonitorRuntimeDiagnosticReader
{
    private const BindingFlags RuntimeFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ConditionalWeakTable<object, FirstObservation> FirstObservations = new();

    public static IReadOnlyList<MonitorRuntimeDiagnostic> Capture(ChatGptMonitorService monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var runningField = typeof(ChatGptMonitorService).GetField("_running", RuntimeFlags);
        if (runningField?.GetValue(monitor) is not System.Collections.IDictionary running)
            return Array.Empty<MonitorRuntimeDiagnostic>();

        var now = DateTimeOffset.UtcNow;
        var result = new List<MonitorRuntimeDiagnostic>(running.Count);
        foreach (System.Collections.DictionaryEntry entry in running)
        {
            var runtime = entry.Value;
            if (runtime is null) continue;

            var runtimeType = runtime.GetType();
            var worker = runtimeType.GetProperty("Worker")?.GetValue(runtime) as Task;
            var cancellation = runtimeType.GetProperty("Cancellation")?.GetValue(runtime) as CancellationTokenSource;
            var stopOwnsCleanup = runtimeType.GetProperty("StopOwnsCleanup")?.GetValue(runtime) is true;
            var firstObserved = FirstObservations.GetValue(runtime, _ => new FirstObservation(now)).Utc;

            var lifecycle = Classify(worker, cancellation?.IsCancellationRequested == true, stopOwnsCleanup);
            result.Add(new MonitorRuntimeDiagnostic(
                Convert.ToInt64(entry.Key, System.Globalization.CultureInfo.InvariantCulture),
                lifecycle,
                worker?.Status.ToString() ?? "Unknown",
                worker?.IsCompleted ?? false,
                worker?.IsFaulted ?? false,
                cancellation?.IsCancellationRequested ?? false,
                firstObserved,
                Math.Max(0, (now - firstObserved).TotalSeconds)));
        }

        return result;
    }

    internal static string Classify(Task? worker, bool cancellationRequested, bool stopOwnsCleanup)
    {
        if (worker is null) return "Unknown";
        if (worker.IsFaulted) return "Faulted";
        if (cancellationRequested || stopOwnsCleanup) return "Stopping";
        if (worker.IsCanceled) return "Canceled";
        if (worker.IsCompleted) return "Completed";
        return "Running";
    }

    private sealed record FirstObservation(DateTimeOffset Utc);
}
