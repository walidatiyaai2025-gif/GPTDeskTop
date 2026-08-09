using System.Reflection;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ConfigurationBackupSetting(string Key, string Value);

public sealed record ConfigurationBackupMonitor(
    string Title,
    string Url,
    string AutoReply,
    int ReplyDelaySeconds,
    int TimerSeconds,
    bool Enabled,
    bool ConversationRotationEnabled,
    string NewChatStartMessage,
    int NewChatDelaySeconds,
    int RotationCooldownSeconds,
    int MaxConversationRotations,
    bool ModelRoutingEnabled,
    string PreferredModel,
    string FallbackModel);

public sealed record ConfigurationBackupDocument(
    string SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string AppVersion,
    string SensitivityNotice,
    IReadOnlyList<ConfigurationBackupSetting> Settings,
    IReadOnlyList<ConfigurationBackupMonitor> Monitors,
    IReadOnlyList<string> Exclusions);

public sealed class ConfigurationBackupService
{
    public const string SchemaVersion = "1.0";

    public const string SensitivityNotice =
        "This is a full configuration backup. It may contain ChatGPT conversation URLs and configured message text. " +
        "Store it as sensitive operator data. It is not the privacy-safe Support Bundle.";

    public static readonly IReadOnlyList<string> AllowedSettingKeys = new[]
    {
        "DefaultAutoReply",
        "DefaultMonitorDelaySeconds",
        "DefaultMonitorTimerSeconds",
        "DefaultConversationRotationEnabled",
        "DefaultNewChatStartMessage",
        "DefaultNewChatDelaySeconds",
        "DefaultRotationCooldownSeconds",
        "DefaultMaxConversationRotations",
        "DefaultModelRoutingEnabled",
        "DefaultPreferredModel",
        "DefaultFallbackModel",
        "HandoffEnabled",
        "HandoffMaxChars",
        "RotateAfterAssistantMessages",
        "MessageCountRotationStartMessage",
        "NoResponseRefreshSeconds",
        "TimeoutRecoveryMessage",
        "NotificationDurationSeconds",
        "NotificationSoundEnabled",
        "NotificationSoundType"
    };

    public static readonly IReadOnlyList<string> Exclusions = new[]
    {
        "Stored History and ConversationRotations history",
        "runtime Chrome Tab IDs and SQLite monitor IDs",
        "monitor rotation counters and crash/recovery/runtime state",
        "UI layout and expansion state",
        "raw SQLite database contents and exception logs",
        "Windows user name, machine name and local profile identity",
        "development-plan message catalog and schedule files (not part of schema 1.0)"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly LocalDatabase _database;

    public ConfigurationBackupService(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<string> ExportAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("A configuration backup output path is required.", nameof(outputPath));

        outputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The configuration backup destination directory is invalid.");

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var document = await CollectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             32 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(Serialize(document).AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Temporary cleanup must never hide the original export failure.
            }
        }
    }

    public async Task<ConfigurationBackupDocument> CollectAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _database
            .ReadConfigurationBackupSnapshotAsync(AllowedSettingKeys, cancellationToken)
            .ConfigureAwait(false);
        return CreateDocument(
            snapshot.Settings,
            snapshot.Monitors,
            DateTimeOffset.UtcNow,
            GetAppVersion());
    }

    public static ConfigurationBackupDocument CreateDocument(
        IReadOnlyDictionary<string, string?> settings,
        IEnumerable<SavedMonitor> monitors,
        DateTimeOffset exportedAtUtc,
        string appVersion)
    {
        settings ??= new Dictionary<string, string?>();
        monitors ??= Array.Empty<SavedMonitor>();

        var monitorSnapshot = monitors.ToArray();
        var invalidIdentity = monitorSnapshot.FirstOrDefault(monitor =>
            !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url));
        if (invalidIdentity is not null)
        {
            throw new InvalidOperationException(
                $"Configuration backup cannot be created because monitor #{invalidIdentity.Id} does not have a stable ChatGPT conversation identity. Use Runtime Health Repair before exporting a portable backup.");
        }

        var duplicateMonitorIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitorSnapshot);
        if (duplicateMonitorIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration backup cannot be created while {duplicateMonitorIds.Count} monitors are blocked by duplicate ChatGPT conversation ownership. Use Runtime Health Repair before exporting a portable backup.");
        }

        var projectedSettings = AllowedSettingKeys
            .Where(key => settings.TryGetValue(key, out var value) && value is not null)
            .Select(key => new ConfigurationBackupSetting(key, settings[key] ?? string.Empty))
            .ToArray();

        var projectedMonitors = monitorSnapshot
            .Select(CreateMonitorBackup)
            .ToArray();

        return new ConfigurationBackupDocument(
            SchemaVersion,
            exportedAtUtc.ToUniversalTime(),
            string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion.Trim(),
            SensitivityNotice,
            projectedSettings,
            projectedMonitors,
            Exclusions);
    }

    public static string Serialize(ConfigurationBackupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static ConfigurationBackupMonitor CreateMonitorBackup(SavedMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return new ConfigurationBackupMonitor(
            monitor.Title ?? string.Empty,
            ChatGptConversationIdentity.Normalize(monitor.Url ?? string.Empty),
            monitor.AutoReply ?? string.Empty,
            Math.Clamp(monitor.ReplyDelaySeconds, 0, 300),
            Math.Clamp(monitor.TimerSeconds, 1, 60),
            monitor.Enabled,
            monitor.ConversationRotationEnabled,
            monitor.NewChatStartMessage ?? string.Empty,
            Math.Clamp(monitor.NewChatDelaySeconds, 0, 600),
            Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600),
            Math.Clamp(monitor.MaxConversationRotations, 0, 1000),
            monitor.ModelRoutingEnabled,
            string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel,
            string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel);
    }

    private static string GetAppVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
}
