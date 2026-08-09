using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record SupportBundleConfigurationSnapshot(
    string DebuggingEndpointKind,
    string DebuggingScheme,
    int DebuggingPort,
    string StartUrlKind,
    int PollIntervalMilliseconds,
    int StableResponseMilliseconds,
    int DelayAfterSendMilliseconds,
    string DatabaseFileName);

public sealed record SupportBundleChromeSnapshot(
    bool Reachable,
    int OpenPageCount,
    int ChatGptTabCount,
    string? FailureType);

public sealed record SupportBundleCount(string Key, int Count);

public sealed record SupportBundleDatabaseSnapshot(
    bool Reachable,
    int SavedMonitorCount,
    int EnabledMonitorCount,
    int RunningMonitorCount,
    int RotationEnabledCount,
    int ModelRoutingEnabledCount,
    int RecentHistoryCount,
    DateTimeOffset? OldestHistoryAt,
    DateTimeOffset? LatestHistoryAt,
    IReadOnlyList<SupportBundleCount> DirectionCounts,
    IReadOnlyList<SupportBundleCount> StatusCounts,
    string? FailureType)
{
    public bool CrashRecoveryPending { get; init; }
    public int InvalidMonitorIdentityCount { get; init; }
}

public sealed record SupportBundleExceptionMetadata(
    bool Exists,
    string FileName,
    long LengthBytes,
    DateTimeOffset? LastWriteUtc);

public sealed record SupportBundleSnapshot(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string AppVersion,
    string Framework,
    string OperatingSystem,
    string ProcessArchitecture,
    string RuntimeHealth,
    string RuntimeHealthSummary,
    SupportBundleConfigurationSnapshot Configuration,
    SupportBundleChromeSnapshot Chrome,
    SupportBundleDatabaseSnapshot Database,
    SupportBundleExceptionMetadata ExceptionLog,
    IReadOnlyList<string> PrivacyExclusions);

public sealed class SupportBundleService
{
    public static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(5);

    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;
    private readonly AppConfig _config;

    public SupportBundleService(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database,
        AppConfig config)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task<string> CreateAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("A support bundle output path is required.", nameof(outputPath));

        outputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The support bundle destination directory is invalid.");

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var snapshot = await CollectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteTextEntryAsync(
                    archive,
                    "diagnostics.json",
                    SerializeSnapshot(snapshot),
                    cancellationToken).ConfigureAwait(false);

                await WriteTextEntryAsync(
                    archive,
                    "README.txt",
                    BuildReadme(snapshot),
                    cancellationToken).ConfigureAwait(false);
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
                // Temporary cleanup must not hide the original bundle failure.
            }
        }
    }

    public async Task<SupportBundleSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(CollectionTimeout);

        var chromeTask = CollectChromeAsync(probeCts.Token);
        var databaseTask = CollectDatabaseAsync(probeCts.Token);
        await Task.WhenAll(chromeTask, databaseTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var chrome = await chromeTask.ConfigureAwait(false);
        var database = await databaseTask.ConfigureAwait(false);
        var health = RuntimeHealthPresentation.Create(
            chrome.Reachable,
            database.Reachable,
            chrome.ChatGptTabCount,
            database.SavedMonitorCount,
            database.RunningMonitorCount,
            DateTimeOffset.UtcNow,
            chrome.FailureType,
            database.FailureType,
            crashRecoveryPending: database.CrashRecoveryPending,
            invalidMonitorIdentityCount: database.InvalidMonitorIdentityCount);

        return new SupportBundleSnapshot(
            "1.0",
            DateTimeOffset.UtcNow,
            GetAppVersion(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            health.Level.ToString(),
            health.Summary,
            CreateConfigurationSnapshot(_config),
            chrome,
            database,
            GetExceptionMetadata(),
            PrivacyExclusions);
    }

    private async Task<SupportBundleChromeSnapshot> CollectChromeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            return new SupportBundleChromeSnapshot(
                true,
                tabs.Count,
                tabs.Count(tab => RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url)),
                null);
        }
        catch (OperationCanceledException)
        {
            return new SupportBundleChromeSnapshot(false, 0, 0, "Timeout");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "SupportBundle.ChromeProbe");
            return new SupportBundleChromeSnapshot(false, 0, 0, ex.GetType().Name);
        }
    }

    private async Task<SupportBundleDatabaseSnapshot> CollectDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
            var logs = await _database.GetRecentLogsAsync(500, cancellationToken).ConfigureAwait(false);
            var recoveryPending = string.Equals(
                await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken).ConfigureAwait(false),
                "1",
                StringComparison.Ordinal);
            return CreateDatabaseSnapshot(
                monitors,
                logs,
                id => _monitor.IsMonitorRunning(id),
                recoveryPending);
        }
        catch (OperationCanceledException)
        {
            return UnavailableDatabaseSnapshot("Timeout");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "SupportBundle.DatabaseProbe");
            return UnavailableDatabaseSnapshot(ex.GetType().Name);
        }
    }

    public static SupportBundleConfigurationSnapshot CreateConfigurationSnapshot(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var endpoint = TryParseUri(config.Chrome.DebuggingBaseUrl);
        var endpointKind = endpoint is null
            ? "Invalid"
            : endpoint.IsLoopback ? "Loopback" : "Remote";
        var scheme = endpoint?.Scheme ?? "Unknown";
        var port = config.Chrome.DebuggingPort > 0
            ? config.Chrome.DebuggingPort
            : endpoint?.Port ?? 0;

        var startUrlKind = RuntimeHealthPresentation.IsChatGptTabUrl(config.Chrome.StartUrl)
            ? "ChatGPT"
            : TryParseUri(config.Chrome.StartUrl) is { Scheme: "https" }
                ? "OtherHttps"
                : "Other";

        return new SupportBundleConfigurationSnapshot(
            endpointKind,
            scheme,
            Math.Max(0, port),
            startUrlKind,
            Math.Max(0, config.Monitoring.PollIntervalMilliseconds),
            Math.Max(0, config.Monitoring.StableResponseMilliseconds),
            Math.Max(0, config.Monitoring.DelayAfterSendMilliseconds),
            Path.GetFileName(config.Database.FileName ?? string.Empty));
    }

    public static SupportBundleDatabaseSnapshot CreateDatabaseSnapshot(
        IEnumerable<SavedMonitor> monitors,
        IEnumerable<MessageLog> logs,
        Func<long, bool>? isRunning = null,
        bool crashRecoveryPending = false)
    {
        var monitorList = monitors?.ToList() ?? new List<SavedMonitor>();
        var logList = logs?.ToList() ?? new List<MessageLog>();
        isRunning ??= _ => false;

        return new SupportBundleDatabaseSnapshot(
            true,
            monitorList.Count,
            monitorList.Count(monitor => monitor.Enabled),
            monitorList.Count(monitor => monitor.Id > 0 && isRunning(monitor.Id)),
            monitorList.Count(monitor => monitor.ConversationRotationEnabled),
            monitorList.Count(monitor => monitor.ModelRoutingEnabled),
            logList.Count,
            logList.Count == 0 ? null : new DateTimeOffset(logList.Min(log => log.Timestamp)),
            logList.Count == 0 ? null : new DateTimeOffset(logList.Max(log => log.Timestamp)),
            Aggregate(logList.Select(log => log.Direction)),
            Aggregate(logList.Select(log => log.Status)),
            null)
        {
            CrashRecoveryPending = crashRecoveryPending,
            InvalidMonitorIdentityCount = monitorList.Count(monitor =>
                !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
        };
    }

    public static string SerializeSnapshot(SupportBundleSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });

    public static string BuildReadme(SupportBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder();
        builder.AppendLine("GPTDeskTop Privacy-Safe Support Bundle");
        builder.AppendLine("=====================================");
        builder.AppendLine();
        builder.AppendLine($"Generated (UTC): {snapshot.GeneratedAtUtc:O}");
        builder.AppendLine($"Application: GPTDeskTop {snapshot.AppVersion}");
        builder.AppendLine($"Runtime health: {snapshot.RuntimeHealth} - {snapshot.RuntimeHealthSummary}");
        builder.AppendLine();
        builder.AppendLine("Contents:");
        builder.AppendLine("- diagnostics.json: environment, sanitized configuration, runtime/recovery health counts, history aggregates and exception-file metadata.");
        builder.AppendLine("- README.txt: this privacy and contents notice.");
        builder.AppendLine();
        builder.AppendLine("Intentionally excluded:");
        foreach (var item in snapshot.PrivacyExclusions) builder.AppendLine($"- {item}");
        builder.AppendLine();
        builder.AppendLine("This bundle is designed for troubleshooting without exporting conversation content or the local SQLite database.");
        return builder.ToString();
    }

    private static readonly IReadOnlyList<string> PrivacyExclusions = new[]
    {
        "ChatGPT prompts and assistant responses",
        "monitor titles, tab IDs and conversation URLs",
        "auto-reply, handoff, recovery and new-chat message text",
        "raw SQLite database contents",
        "raw exception log contents",
        "Windows user name, machine name and local profile paths"
    };

    private static SupportBundleDatabaseSnapshot UnavailableDatabaseSnapshot(string failureType)
        => new(
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            Array.Empty<SupportBundleCount>(),
            Array.Empty<SupportBundleCount>(),
            failureType);

    private static IReadOnlyList<SupportBundleCount> Aggregate(IEnumerable<string?> values)
        => values
            .Select(value => string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SupportBundleCount(group.Key, group.Count()))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static SupportBundleExceptionMetadata GetExceptionMetadata()
    {
        try
        {
            var path = ExceptionLogService.GetTodayLogPath();
            var info = new FileInfo(path);
            return info.Exists
                ? new SupportBundleExceptionMetadata(true, info.Name, info.Length, info.LastWriteTimeUtc)
                : new SupportBundleExceptionMetadata(false, info.Name, 0, null);
        }
        catch
        {
            return new SupportBundleExceptionMetadata(false, "exceptions-current.log", 0, null);
        }
    }

    private static Uri? TryParseUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static string GetAppVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}