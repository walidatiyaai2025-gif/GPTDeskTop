using System.Security.Cryptography;
using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Persisted per-monitor ownership switch for Development Messages.
/// The stable conversation key lets a brand-new monitor persist the operator's
/// choice before SQLite assigns its numeric monitor ID; the ID key is migrated
/// automatically as soon as the saved monitor is resolved.
/// </summary>
public static class DevelopmentPlanMonitorSettings
{
    private const string MonitorPrefix = "TaskAutomation.Monitor.";
    private const string ConversationPrefix = "TaskAutomation.Conversation.";
    private const string Suffix = ".Enabled";
    private static LocalDatabase? _configuredDatabase;

    public static void ConfigureDatabase(LocalDatabase database)
        => Volatile.Write(ref _configuredDatabase, database ?? throw new ArgumentNullException(nameof(database)));

    public static string Key(long monitorId)
    {
        if (monitorId <= 0) throw new ArgumentOutOfRangeException(nameof(monitorId));
        return $"{MonitorPrefix}{monitorId}{Suffix}";
    }

    public static string ConversationKey(string? conversationUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(conversationUrl) ? string.Empty : conversationUrl.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"{ConversationPrefix}{hash}{Suffix}";
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

    public static async Task<bool> IsEnabledAsync(
        LocalDatabase database,
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitor);

        string? idValue = null;
        if (monitor.Id > 0)
        {
            idValue = await database.GetSettingAsync(Key(monitor.Id), cancellationToken).ConfigureAwait(false);
            if (idValue is not null)
                return IsEnabledValue(idValue);
        }

        var conversationValue = await database.GetSettingAsync(
            ConversationKey(monitor.Url), cancellationToken).ConfigureAwait(false);
        var enabled = IsEnabledValue(conversationValue);
        if (monitor.Id > 0 && conversationValue is not null)
            await database.SetSettingAsync(Key(monitor.Id), enabled ? "1" : "0", cancellationToken).ConfigureAwait(false);
        return enabled;
    }

    public static async Task SetEnabledAsync(
        LocalDatabase database,
        SavedMonitor monitor,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitor);
        var value = enabled ? "1" : "0";
        await database.SetSettingAsync(ConversationKey(monitor.Url), value, cancellationToken).ConfigureAwait(false);
        if (monitor.Id > 0)
            await database.SetSettingAsync(Key(monitor.Id), value, cancellationToken).ConfigureAwait(false);
        monitor.UseDevelopmentMessages = enabled;
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

    public static bool ReadForDialog(SavedMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var database = Volatile.Read(ref _configuredDatabase);
        if (database is null) return monitor.UseDevelopmentMessages;
        try
        {
            var enabled = IsEnabledAsync(database, monitor).GetAwaiter().GetResult();
            monitor.UseDevelopmentMessages = enabled;
            return enabled;
        }
        catch
        {
            return monitor.UseDevelopmentMessages;
        }
    }

    public static void PersistFromDialog(SavedMonitor monitor, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        monitor.UseDevelopmentMessages = enabled;
        var database = Volatile.Read(ref _configuredDatabase);
        if (database is null) return;
        SetEnabledAsync(database, monitor, enabled).GetAwaiter().GetResult();
    }

    public static bool IsEnabledValue(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}
