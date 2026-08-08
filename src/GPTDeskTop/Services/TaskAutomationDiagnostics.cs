using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

/// <summary>
/// Provides a compact persisted snapshot for UI/status surfaces without exposing
/// the automation worker implementation to presentation code.
/// </summary>
public sealed record TaskAutomationDiagnostics(
    string Phase,
    DateTimeOffset? WorkWindowStartedUtc,
    DateTimeOffset? CoolingStartedUtc,
    DateTimeOffset? LastCycleCompletedUtc,
    int LastCycleSentCount,
    string? LastError,
    IReadOnlyDictionary<long, TaskMonitorCheckpoint> Checkpoints);

public sealed record TaskMonitorCheckpoint(
    string Status,
    int MessageIndex,
    string Message,
    DateTimeOffset? Timestamp,
    int NextMessageIndex);

public static class TaskAutomationDiagnosticsReader
{
    public static async Task<TaskAutomationDiagnostics> ReadAsync(
        LocalDatabase database,
        IReadOnlyCollection<long> monitorIds,
        CancellationToken cancellationToken = default)
    {
        var phase = await Get(database, "TaskAutomation.Phase", "Idle", cancellationToken);
        var workStarted = await GetDate(database, "TaskAutomation.WorkWindowStartedUtc", cancellationToken);
        var coolingStarted = await GetDate(database, "TaskAutomation.CoolingStartedUtc", cancellationToken);
        var lastCycle = await GetDate(database, "TaskAutomation.LastCycleCompletedUtc", cancellationToken);
        var sentRaw = await Get(database, "TaskAutomation.LastCycleSentCount", "0", cancellationToken);
        var sent = int.TryParse(sentRaw, out var parsedSent) ? Math.Max(0, parsedSent) : 0;
        var lastError = await Get(database, "TaskAutomation.LastError", "", cancellationToken);

        var checkpoints = new Dictionary<long, TaskMonitorCheckpoint>();
        foreach (var monitorId in monitorIds)
        {
            var status = await Get(database, $"TaskAutomation.Checkpoint.{monitorId}.Status", "", cancellationToken);
            var indexRaw = await Get(database, $"TaskAutomation.Checkpoint.{monitorId}.MessageIndex", "0", cancellationToken);
            var nextRaw = await Get(database, $"TaskAutomation.Monitor.{monitorId}.NextMessageIndex", "0", cancellationToken);
            var message = await Get(database, $"TaskAutomation.Checkpoint.{monitorId}.Message", "", cancellationToken);
            var timestamp = await GetDate(database, $"TaskAutomation.Checkpoint.{monitorId}.Utc", cancellationToken);

            checkpoints[monitorId] = new TaskMonitorCheckpoint(
                status,
                ParseNonNegative(indexRaw),
                message,
                timestamp,
                ParseNonNegative(nextRaw));
        }

        return new TaskAutomationDiagnostics(
            phase,
            workStarted,
            coolingStarted,
            lastCycle,
            sent,
            string.IsNullOrWhiteSpace(lastError) ? null : lastError,
            checkpoints);
    }

    private static async Task<string> Get(LocalDatabase database, string key, string fallback, CancellationToken cancellationToken)
        => await database.GetSettingAsync(key, cancellationToken).ConfigureAwait(false) ?? fallback;

    private static async Task<DateTimeOffset?> GetDate(LocalDatabase database, string key, CancellationToken cancellationToken)
    {
        var raw = await database.GetSettingAsync(key, cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }

    private static int ParseNonNegative(string value)
        => int.TryParse(value, out var parsed) ? Math.Max(0, parsed) : 0;
}
