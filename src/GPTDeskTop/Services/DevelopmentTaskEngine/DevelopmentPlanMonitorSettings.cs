using GPTDeskTop.Data;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Persisted per-monitor ownership switch for Development Messages.
/// When enabled, the DevelopmentTaskEngine owns continuation delivery and the
/// monitor's legacy single AutoReply must remain passive.
/// </summary>
public static class DevelopmentPlanMonitorSettings
{
    private const string Prefix = "TaskAutomation.Monitor.";
    private const string Suffix = ".Enabled";

    public static string Key(long monitorId)
    {
        if (monitorId <= 0) throw new ArgumentOutOfRangeException(nameof(monitorId));
        return $"{Prefix}{monitorId}{Suffix}";
    }

    public static async Task<bool> IsEnabledAsync(
        LocalDatabase database,
        long monitorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (monitorId <= 0) return false;
        var value = await database.GetSettingAsync(Key(monitorId), cancellationToken).ConfigureAwait(false);
        return IsEnabledValue(value);
    }

    public static Task SetEnabledAsync(
        LocalDatabase database,
        long monitorId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.SetSettingAsync(Key(monitorId), enabled ? "1" : "0", cancellationToken);
    }

    public static bool IsEnabledValue(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}
