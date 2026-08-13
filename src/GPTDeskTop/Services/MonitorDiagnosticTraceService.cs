using System.Text;
using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Writes a bounded, privacy-safe operational timeline that can be attached to support bundles.
/// The trace intentionally excludes chat text, monitor/tab titles, URLs, tab IDs and raw exception messages.
/// </summary>
public sealed class MonitorDiagnosticTraceService : IDisposable
{
    private const long MaxTraceBytes = 4L * 1024 * 1024;
    private const int DefaultBundleTailBytes = 768 * 1024;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object InstanceSync = new();
    private static readonly object FileSync = new();
    private static MonitorDiagnosticTraceService? _instance;

    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<long, string> _lastStateFingerprints = new();
    private readonly Task _worker;
    private long _lastHistoryId;
    private DateTimeOffset _lastHeartbeatUtc = DateTimeOffset.MinValue;
    private int _disposed;

    public static string CurrentFilePath => Path.Combine(AppContext.BaseDirectory, "logs", "monitor-diagnostics.jsonl");
    public static string PreviousFilePath => CurrentFilePath + ".1";

    private MonitorDiagnosticTraceService(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _database = database ?? throw new ArgumentNullException(nameof(database));

        _monitor.HistoryChanged += SignalCapture;
        _monitor.RunningStateChanged += SignalCapture;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        WriteRecord(new TraceRecord(
            DateTimeOffset.UtcNow,
            "trace-start",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        _worker = Task.Run(() => RunAsync(_shutdown.Token));
    }

    /// <summary>
    /// Starts one process-wide trace collector. Repeated calls are harmless.
    /// </summary>
    public static void EnsureStarted(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database)
    {
        lock (InstanceSync)
        {
            _instance ??= new MonitorDiagnosticTraceService(chrome, monitor, database);
        }
    }

    public static string ReadBundleTail(int maxBytes = DefaultBundleTailBytes)
    {
        maxBytes = Math.Clamp(maxBytes, 16 * 1024, 2 * 1024 * 1024);
        try
        {
            lock (FileSync)
            {
                if (!File.Exists(CurrentFilePath)) return string.Empty;
                using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytesToRead = (int)Math.Min(stream.Length, maxBytes);
                if (bytesToRead <= 0) return string.Empty;

                stream.Seek(-bytesToRead, SeekOrigin.End);
                var buffer = new byte[bytesToRead];
                var total = 0;
                while (total < buffer.Length)
                {
                    var read = stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0) break;
                    total += read;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, total);
                if (stream.Length > bytesToRead)
                {
                    var newline = text.IndexOf('\n');
                    if (newline >= 0 && newline + 1 < text.Length)
                        text = text[(newline + 1)..];
                }
                return text;
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    public static DiagnosticHistoryRecord CreateHistoryRecord(MessageLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return new DiagnosticHistoryRecord(
            log.Timestamp.Kind == DateTimeKind.Unspecified
                ? new DateTimeOffset(DateTime.SpecifyKind(log.Timestamp, DateTimeKind.Local))
                : new DateTimeOffset(log.Timestamp),
            "history",
            log.MonitorId,
            log.Id,
            SafeToken(log.Direction),
            SafeToken(log.Status));
    }

    public static DiagnosticStateRecord CreateStateRecord(
        long monitorId,
        bool enabled,
        bool running,
        bool targetFound,
        ChatPageState? pageState,
        string? failureType = null)
        => new(
            DateTimeOffset.UtcNow,
            "monitor-state",
            monitorId,
            enabled,
            running,
            targetFound,
            pageState?.AssistantCount,
            pageState?.IsGenerating,
            pageState is null ? null : !string.IsNullOrWhiteSpace(pageState.LastAssistantText),
            pageState is null ? null : !string.IsNullOrWhiteSpace(pageState.ErrorText),
            SafeFailureType(failureType));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CaptureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                WriteRecord(new TraceRecord(
                    DateTimeOffset.UtcNow,
                    "collector-failure",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    ex.GetType().Name));
            }

            try
            {
                await _wake.WaitAsync(SampleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        await CaptureHistoryAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SavedMonitor> savedMonitors;
        IReadOnlyList<ChromeTab> tabs;
        try
        {
            savedMonitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteSubsystemFailure("database-snapshot", ex);
            return;
        }

        try
        {
            tabs = await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteSubsystemFailure("chrome-targets", ex);
            foreach (var saved in savedMonitors.Where(item => item.Id > 0 && _monitor.IsMonitorRunning(item.Id)))
                WriteStateIfChanged(CreateStateRecord(saved.Id, saved.Enabled, true, false, null, ex.GetType().Name));
            return;
        }

        var activeIds = new HashSet<long>();
        foreach (var saved in savedMonitors.Where(item => item.Id > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var running = _monitor.IsMonitorRunning(saved.Id);
            if (!running && !saved.Enabled) continue;
            activeIds.Add(saved.Id);

            var tab = tabs.FirstOrDefault(candidate =>
                RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)
                && RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)
                && ChatGptConversationIdentity.IsSame(candidate.Url, saved.Url));

            if (!running || tab is null)
            {
                WriteStateIfChanged(CreateStateRecord(saved.Id, saved.Enabled, running, tab is not null, null));
                continue;
            }

            try
            {
                var state = await _chrome.GetChatStateAsync(tab, cancellationToken).ConfigureAwait(false);
                WriteStateIfChanged(CreateStateRecord(saved.Id, saved.Enabled, true, true, state));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WriteStateIfChanged(CreateStateRecord(saved.Id, saved.Enabled, true, true, null, ex.GetType().Name));
            }
        }

        foreach (var staleId in _lastStateFingerprints.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            _lastStateFingerprints.Remove(staleId);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastHeartbeatUtc >= HeartbeatInterval)
        {
            _lastHeartbeatUtc = now;
            WriteRecord(new TraceRecord(
                now,
                "heartbeat",
                null,
                null,
                null,
                null,
                savedMonitors.Count,
                savedMonitors.Count(item => item.Id > 0 && _monitor.IsMonitorRunning(item.Id)),
                tabs.Count,
                null,
                null,
                null,
                null));
        }
    }

    private async Task CaptureHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MessageLog> recent;
        try
        {
            recent = await _database.GetRecentLogsAsync(250, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteSubsystemFailure("history-snapshot", ex);
            return;
        }

        foreach (var log in recent.Where(item => item.Id > _lastHistoryId).OrderBy(item => item.Id))
        {
            WriteRecord(CreateHistoryRecord(log));
            _lastHistoryId = Math.Max(_lastHistoryId, log.Id);
        }

        if (_lastHistoryId == 0 && recent.Count > 0)
            _lastHistoryId = recent.Max(item => item.Id);
    }

    private void WriteStateIfChanged(DiagnosticStateRecord record)
    {
        var fingerprint = string.Join('|',
            record.Enabled,
            record.Running,
            record.TargetFound,
            record.AssistantCount,
            record.IsGenerating,
            record.HasAssistantText,
            record.HasRenderedError,
            record.FailureType ?? string.Empty);

        if (_lastStateFingerprints.TryGetValue(record.MonitorId, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
            return;

        _lastStateFingerprints[record.MonitorId] = fingerprint;
        WriteRecord(record);
    }

    private static void WriteSubsystemFailure(string eventType, Exception ex)
        => WriteRecord(new TraceRecord(
            DateTimeOffset.UtcNow,
            eventType,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ex.GetType().Name));

    private void SignalCapture()
    {
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void WriteRecord<T>(T record)
    {
        try
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            lock (FileSync)
            {
                var directory = Path.GetDirectoryName(CurrentFilePath)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded();
                File.AppendAllText(CurrentFilePath, json + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never affect monitor execution.
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(CurrentFilePath);
        if (!info.Exists || info.Length < MaxTraceBytes) return;
        try
        {
            if (File.Exists(PreviousFilePath)) File.Delete(PreviousFilePath);
            File.Move(CurrentFilePath, PreviousFilePath);
        }
        catch
        {
            // Keep appending to the current file if rotation is temporarily unavailable.
        }
    }

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(blank)";
        var trimmed = value.Trim();
        if (trimmed.Length > 64) return "Other";
        foreach (var character in trimmed)
        {
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '(' or ')' or ' '))
                return "Other";
        }
        return trimmed;
    }

    private static string? SafeFailureType(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : SafeToken(value);

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _monitor.HistoryChanged -= SignalCapture;
        _monitor.RunningStateChanged -= SignalCapture;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _shutdown.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _shutdown.Dispose();
        _wake.Dispose();
    }

    private sealed record TraceRecord(
        DateTimeOffset TimestampUtc,
        string Event,
        long? MonitorId,
        long? HistoryLogId,
        string? Direction,
        string? Status,
        int? SavedMonitorCount,
        int? RunningMonitorCount,
        int? OpenPageCount,
        int? AssistantCount,
        bool? IsGenerating,
        bool? HasRenderedError,
        string? FailureType);
}

public sealed record DiagnosticHistoryRecord(
    DateTimeOffset TimestampUtc,
    string Event,
    long? MonitorId,
    long HistoryLogId,
    string Direction,
    string Status);

public sealed record DiagnosticStateRecord(
    DateTimeOffset TimestampUtc,
    string Event,
    long MonitorId,
    bool Enabled,
    bool Running,
    bool TargetFound,
    int? AssistantCount,
    bool? IsGenerating,
    bool? HasAssistantText,
    bool? HasRenderedError,
    string? FailureType);
