namespace GPTDeskTop.Services;

public sealed record SavedMonitorLiveState(
    string Status,
    string Reason,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Maps runtime-only monitor and delivery signals into operator-facing dashboard state.
/// The projection deliberately never echoes the source activity string: known operational
/// markers are translated to fixed text so prompt/auto-reply/title/URL content cannot leak
/// into the Saved Monitors health grid.
/// </summary>
public static class SavedMonitorLivePresentation
{
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(2);

    public static SavedMonitorLiveState? FromActivity(string? activity, DateTimeOffset observedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(activity))
            return null;

        if (Contains(activity, "response-still-growing")
            || Contains(activity, "assistant response still growing")
            || Contains(activity, "chatgpt-generating")
            || Contains(activity, "chatgpt is generating"))
        {
            return State("🟢 Generating", "ChatGPT is generating the monitored response.", observedAtUtc);
        }

        if (Contains(activity, "new response detected")
            || Contains(activity, "response-detected"))
        {
            return State("🟢 Observing", "New ChatGPT response detected; waiting for a stable response.", observedAtUtc);
        }

        if (Contains(activity, "response-stable")
            || Contains(activity, "assistant response stable"))
        {
            return State("🟢 Monitoring", "Stable assistant response observed; monitoring continues.", observedAtUtc);
        }

        if (Contains(activity, "verified message accepted")
            || Contains(activity, "verified-receipt-confirmed"))
        {
            return State("🟢 Delivered", "Outbound message receipt confirmed; waiting for ChatGPT.", observedAtUtc);
        }

        if (Contains(activity, "exactly-once guard")
            || Contains(activity, "uncertain-or-in-flight"))
        {
            return State("🟢 Reconciling", "Delivery is awaiting confirmation; duplicate sending remains blocked.", observedAtUtc);
        }

        if (Contains(activity, "recovery complete")
            || Contains(activity, "is now monitored")
            || Contains(activity, "same monitor id is now bound")
            || activity.StartsWith("Started:", StringComparison.OrdinalIgnoreCase))
        {
            return State("🟢 Monitoring", "Monitor is running and observing its ChatGPT conversation.", observedAtUtc);
        }

        return null;
    }

    public static SavedMonitorLiveState FromDelivery(
        string phase,
        int physicalSendCount,
        DateTimeOffset observedAtUtc)
    {
        var attempts = Math.Max(physicalSendCount, 0);
        return phase switch
        {
            "Sending" => State(
                "🟢 Sending",
                attempts > 0
                    ? $"Sending to ChatGPT; physical submit attempt {attempts} is being verified."
                    : "Sending to ChatGPT; the physical submit is being verified.",
                observedAtUtc),
            "Accepted" => State(
                "🟢 Delivered",
                "Outbound message receipt confirmed; waiting for ChatGPT response.",
                observedAtUtc),
            "ReconcileRequired" => State(
                "🟢 Reconciling",
                "Receipt was not confirmed; waiting for response evidence with duplicate sending blocked.",
                observedAtUtc),
            "Completed" => State(
                "🟢 Monitoring",
                "Assistant response observed; the previous delivery is reconciled and monitoring continues.",
                observedAtUtc),
            _ => State(
                "🟢 Monitoring",
                "Monitor runtime is active.",
                observedAtUtc)
        };
    }

    public static SavedMonitorRowHealth Overlay(
        SavedMonitorRowHealth baseline,
        bool workerRunning,
        SavedMonitorLiveState? live,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (!workerRunning || live is null || nowUtc - live.ObservedAtUtc > FreshnessWindow)
            return baseline;

        // A verified live signal may override only a healthy row or transient health-probe
        // states. It must never hide true ownership/configuration/connection/rendered-error
        // failures.
        if (baseline.IsHealthy
            || string.Equals(baseline.Status, "🔴 Recovering", StringComparison.Ordinal)
            || string.Equals(baseline.Status, "🔴 Checking", StringComparison.Ordinal))
        {
            return new SavedMonitorRowHealth(true, live.Status, live.Reason);
        }

        return baseline;
    }

    private static SavedMonitorLiveState State(string status, string reason, DateTimeOffset at)
        => new(status, reason, at);

    private static bool Contains(string value, string marker)
        => value.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
