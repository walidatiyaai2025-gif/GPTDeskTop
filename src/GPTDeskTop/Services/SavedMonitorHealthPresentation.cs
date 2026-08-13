using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record SavedMonitorRowHealth(
    bool IsHealthy,
    string Status,
    string Reason);

/// <summary>
/// Produces the operator-facing health state used by the Saved Monitors grid.
/// A monitor is green only after its worker, conversation target and ChatGPT page state are all
/// verified. Normal ChatGPT generation/waiting is healthy; transport/recovery/error states are not.
/// </summary>
public static class SavedMonitorHealthPresentation
{
    public static SavedMonitorRowHealth Evaluate(
        SavedMonitor monitor,
        bool workerRunning,
        bool duplicateOwnership,
        bool conversationTabAvailable,
        ChatPageState? pageState,
        string? probeError = null,
        string? runtimeFailureReason = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (!monitor.Enabled)
            return Unhealthy("Disabled", "Disabled in monitor settings.");

        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            return Unhealthy("Invalid", "Saved conversation URL is not a stable ChatGPT conversation.");

        if (duplicateOwnership)
            return Unhealthy("Blocked", "Another saved monitor owns the same ChatGPT conversation.");

        if (!string.IsNullOrWhiteSpace(probeError))
            return Unhealthy("Connection", NormalizeReason(probeError));

        if (!workerRunning)
        {
            if (!string.IsNullOrWhiteSpace(runtimeFailureReason))
                return Unhealthy("Stopped", NormalizeReason(runtimeFailureReason));

            return !conversationTabAvailable
                ? Unhealthy("Stopped", "Conversation tab is not open.")
                : Unhealthy("Stopped", "Monitor worker is not running.");
        }

        if (!conversationTabAvailable)
            return Unhealthy("Recovering", "Saved conversation tab is not currently available.");

        if (pageState is null)
            return Unhealthy("Checking", "Waiting for a verified ChatGPT health reading.");

        if (!string.IsNullOrWhiteSpace(pageState.ErrorText))
            return Unhealthy("Recovery", $"ChatGPT error: {NormalizeReason(pageState.ErrorText)}");

        // A slow or actively-generating ChatGPT response is explicitly a healthy passive wait.
        // Elapsed time alone must never turn the monitor red or trigger recovery.
        return pageState.IsGenerating
            ? Healthy("Monitoring normally — ChatGPT is generating a response.")
            : Healthy("Monitoring normally.");
    }

    private static SavedMonitorRowHealth Healthy(string reason)
        => new(true, "🟢 Healthy", reason);

    private static SavedMonitorRowHealth Unhealthy(string status, string reason)
        => new(false, $"🔴 {status}", reason);

    internal static string NormalizeReason(string value)
    {
        var normalized = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        const int maxLength = 260;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 1)] + "…";
    }
}
