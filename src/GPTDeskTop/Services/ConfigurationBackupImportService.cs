using System.Text.Json;
using System.Text.Json.Serialization;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ConfigurationBackupImportPlan(
    string SourcePath,
    string SchemaVersion,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<SavedMonitor> Monitors);

public sealed class ConfigurationBackupImportService
{
    public const long MaxBackupBytes = 5 * 1024 * 1024;
    public const int MaxMonitorCount = 1000;
    public const int MaxMessageChars = 20000;

    private static readonly HashSet<string> AllowedSettingKeySet =
        new(ConfigurationBackupService.AllowedSettingKeys, StringComparer.Ordinal);

    private static readonly HashSet<string> BooleanSettingKeys = new(StringComparer.Ordinal)
    {
        "DefaultConversationRotationEnabled",
        "DefaultModelRoutingEnabled",
        "HandoffEnabled",
        "NotificationSoundEnabled"
    };

    private static readonly HashSet<string> MessageSettingKeys = new(StringComparer.Ordinal)
    {
        "DefaultAutoReply",
        "DefaultNewChatStartMessage",
        "MessageCountRotationStartMessage",
        "TimeoutRecoveryMessage"
    };

    private static readonly string[] NotificationSounds = { "Asterisk", "Exclamation", "Beep", "Hand" };

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly LocalDatabase _database;

    public ConfigurationBackupImportService(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<ConfigurationBackupImportPlan> LoadPlanAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A configuration backup file path is required.", nameof(sourcePath));

        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The configuration backup file does not exist.", sourcePath);

        var info = new FileInfo(sourcePath);
        if (info.Length <= 0)
            throw new InvalidDataException("The configuration backup file is empty.");
        if (info.Length > MaxBackupBytes)
            throw new InvalidDataException($"The configuration backup exceeds the {MaxBackupBytes / (1024 * 1024)} MB safety limit.");

        var json = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ConfigurationBackupDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ConfigurationBackupDocument>(json, ImportJsonOptions)
                ?? throw new InvalidDataException("The configuration backup JSON did not contain a document.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The configuration backup JSON is malformed or contains unsupported fields.", ex);
        }

        return CreatePlan(sourcePath, document);
    }

    public Task<ConfigurationImportDatabaseResult> ApplyAsync(
        ConfigurationBackupImportPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.SchemaVersion, ConfigurationBackupService.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported configuration backup schema '{plan.SchemaVersion}'. Expected {ConfigurationBackupService.SchemaVersion}.");

        return _database.ApplyConfigurationImportAsync(plan.Settings, plan.Monitors, cancellationToken);
    }

    public async Task<ConfigurationImportDatabaseResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return await ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public static ConfigurationBackupImportPlan CreatePlan(
        string sourcePath,
        ConfigurationBackupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.Equals(document.SchemaVersion, ConfigurationBackupService.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported configuration backup schema '{document.SchemaVersion ?? "<missing>"}'. Expected {ConfigurationBackupService.SchemaVersion}.");

        if (document.Settings is null)
            throw new InvalidDataException("The configuration backup is missing Settings.");
        if (document.Monitors is null)
            throw new InvalidDataException("The configuration backup is missing Monitors.");
        if (document.Monitors.Count > MaxMonitorCount)
            throw new InvalidDataException($"The configuration backup contains more than {MaxMonitorCount} monitors.");

        var settings = ValidateSettings(document.Settings);
        var monitors = ValidateMonitors(document.Monitors);

        return new ConfigurationBackupImportPlan(
            Path.GetFullPath(string.IsNullOrWhiteSpace(sourcePath) ? "." : sourcePath),
            ConfigurationBackupService.SchemaVersion,
            settings,
            monitors);
    }

    private static IReadOnlyDictionary<string, string> ValidateSettings(
        IReadOnlyList<ConfigurationBackupSetting> settings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var setting in settings)
        {
            if (setting is null)
                throw new InvalidDataException("The configuration backup contains a null setting entry.");

            var key = setting.Key?.Trim() ?? string.Empty;
            if (!AllowedSettingKeySet.Contains(key))
                throw new InvalidDataException($"Setting '{key}' is not allowed in configuration backup schema {ConfigurationBackupService.SchemaVersion}.");
            if (result.ContainsKey(key))
                throw new InvalidDataException($"Setting '{key}' appears more than once in the configuration backup.");

            result[key] = NormalizeSettingValue(key, setting.Value ?? string.Empty);
        }

        return result;
    }

    private static string NormalizeSettingValue(string key, string value)
    {
        if (BooleanSettingKeys.Contains(key))
        {
            if (value is not ("0" or "1"))
                throw new InvalidDataException($"Setting '{key}' must be 0 or 1.");
            return value;
        }

        return key switch
        {
            "DefaultMonitorDelaySeconds" => NormalizeInteger(key, value, 0, 300),
            "DefaultMonitorTimerSeconds" => NormalizeInteger(key, value, 1, 60),
            "DefaultNewChatDelaySeconds" => NormalizeInteger(key, value, 0, 600),
            "DefaultRotationCooldownSeconds" => NormalizeInteger(key, value, 0, 3600),
            "DefaultMaxConversationRotations" => NormalizeInteger(key, value, 0, 1000),
            "HandoffMaxChars" => NormalizeInteger(key, value, 1500, 20000),
            "RotateAfterAssistantMessages" => NormalizeInteger(key, value, 0, 10000),
            "NoResponseRefreshSeconds" => NormalizeInteger(key, value, 30, 3600),
            "NotificationDurationSeconds" => NormalizeInteger(key, value, 1, 60),
            "NotificationSoundType" => NormalizeNotificationSound(value),
            "DefaultPreferredModel" or "DefaultFallbackModel" => NormalizeModelLabel(key, value),
            _ when MessageSettingKeys.Contains(key) => NormalizeRequiredMessage(key, value),
            _ => NormalizeBoundedText(key, value)
        };
    }

    private static string NormalizeInteger(string key, string value, int min, int max)
    {
        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
            throw new InvalidDataException($"Setting '{key}' must be an integer between {min} and {max}.");
        return parsed.ToString();
    }

    private static string NormalizeNotificationSound(string value)
    {
        var match = NotificationSounds.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new InvalidDataException("NotificationSoundType must be Asterisk, Exclamation, Beep or Hand.");
        return match;
    }

    private static string NormalizeModelLabel(string key, string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length > 200)
            throw new InvalidDataException($"Setting '{key}' exceeds 200 characters.");
        return string.IsNullOrWhiteSpace(value) ? "Auto" : value;
    }

    private static string NormalizeRequiredMessage(string key, string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Setting '{key}' cannot be empty.");
        if (value.Length > MaxMessageChars)
            throw new InvalidDataException($"Setting '{key}' exceeds {MaxMessageChars} characters.");
        return value;
    }

    private static string NormalizeBoundedText(string key, string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length > MaxMessageChars)
            throw new InvalidDataException($"Setting '{key}' exceeds {MaxMessageChars} characters.");
        return value;
    }

    private static IReadOnlyList<SavedMonitor> ValidateMonitors(
        IReadOnlyList<ConfigurationBackupMonitor> monitors)
    {
        var result = new List<SavedMonitor>(monitors.Count);
        var conversationIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            if (monitor is null)
                throw new InvalidDataException("The configuration backup contains a null monitor entry.");

            var title = (monitor.Title ?? string.Empty).Trim();
            var url = (monitor.Url ?? string.Empty).Trim();
            var autoReply = (monitor.AutoReply ?? string.Empty).Trim();
            var startMessage = (monitor.NewChatStartMessage ?? string.Empty).Trim();
            var preferredModel = string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel.Trim();
            var fallbackModel = string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel.Trim();

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidDataException("A monitor title cannot be empty.");
            if (title.Length > 1000)
                throw new InvalidDataException($"Monitor title '{title[..Math.Min(title.Length, 80)]}' exceeds 1000 characters.");
            if (string.IsNullOrWhiteSpace(url) || url.Length > 4096)
                throw new InvalidDataException($"Monitor '{title}' has an invalid conversation URL length.");
            if (!RuntimeHealthPresentation.IsChatGptConversationUrl(url))
                throw new InvalidDataException($"Monitor '{title}' must use an absolute HTTPS ChatGPT conversation URL with a stable /c/{{conversation-id}} identity.");

            var canonicalUrl = ChatGptConversationIdentity.Normalize(url);
            if (!conversationIdentities.Add(canonicalUrl))
                throw new InvalidDataException($"Conversation identity '{canonicalUrl}' appears more than once in the configuration backup.");
            if (string.IsNullOrWhiteSpace(autoReply) || autoReply.Length > MaxMessageChars)
                throw new InvalidDataException($"Monitor '{title}' has an empty or oversized auto reply.");
            if (monitor.ConversationRotationEnabled && string.IsNullOrWhiteSpace(startMessage))
                throw new InvalidDataException($"Monitor '{title}' requires a new-chat start message while rotation is enabled.");
            if (startMessage.Length > MaxMessageChars)
                throw new InvalidDataException($"Monitor '{title}' has an oversized new-chat start message.");
            if (monitor.ReplyDelaySeconds < 0 || monitor.ReplyDelaySeconds > 300)
                throw new InvalidDataException($"Monitor '{title}' reply delay must be between 0 and 300 seconds.");
            if (monitor.TimerSeconds < 1 || monitor.TimerSeconds > 60)
                throw new InvalidDataException($"Monitor '{title}' polling timer must be between 1 and 60 seconds.");
            if (monitor.NewChatDelaySeconds < 0 || monitor.NewChatDelaySeconds > 600)
                throw new InvalidDataException($"Monitor '{title}' new-chat delay must be between 0 and 600 seconds.");
            if (monitor.RotationCooldownSeconds < 0 || monitor.RotationCooldownSeconds > 3600)
                throw new InvalidDataException($"Monitor '{title}' rotation cooldown must be between 0 and 3600 seconds.");
            if (monitor.MaxConversationRotations < 0 || monitor.MaxConversationRotations > 1000)
                throw new InvalidDataException($"Monitor '{title}' maximum rotations must be between 0 and 1000.");
            if (preferredModel.Length > 200 || fallbackModel.Length > 200)
                throw new InvalidDataException($"Monitor '{title}' contains an oversized model label.");

            result.Add(new SavedMonitor
            {
                Id = 0,
                TabId = string.Empty,
                Title = title,
                Url = canonicalUrl,
                AutoReply = autoReply,
                ReplyDelaySeconds = monitor.ReplyDelaySeconds,
                TimerSeconds = monitor.TimerSeconds,
                Enabled = monitor.Enabled,
                ConversationRotationEnabled = monitor.ConversationRotationEnabled,
                NewChatStartMessage = startMessage,
                NewChatDelaySeconds = monitor.NewChatDelaySeconds,
                RotationCooldownSeconds = monitor.RotationCooldownSeconds,
                MaxConversationRotations = monitor.MaxConversationRotations,
                RotationCount = 0,
                ModelRoutingEnabled = monitor.ModelRoutingEnabled,
                PreferredModel = preferredModel,
                FallbackModel = fallbackModel
            });
        }

        return result;
    }

}