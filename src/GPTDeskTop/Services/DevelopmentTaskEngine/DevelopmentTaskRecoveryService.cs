namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Restores a persisted development-task checkpoint after process restart.
/// It never invents a new tab when the persisted monitor/tab can be recovered.
/// </summary>
public sealed class DevelopmentTaskRecoveryService
{
    private readonly DevelopmentTaskEngine _engine;

    public DevelopmentTaskRecoveryService(DevelopmentTaskEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public async Task<DevelopmentTaskRecoveryResult> RestoreAsync(
        DevelopmentTaskState checkpoint,
        string monitorId,
        string tabId,
        Func<string, Task<bool>> tabExistsAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(tabExistsAsync);

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(checkpoint.LastMonitorId, monitorId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.LastTabId, tabId, StringComparison.Ordinal))
        {
            return DevelopmentTaskRecoveryResult.Rejected("Persisted monitor/tab does not match the recovery target.");
        }

        var tabExists = await tabExistsAsync(tabId).ConfigureAwait(false);
        if (!tabExists)
        {
            return DevelopmentTaskRecoveryResult.Rejected("Persisted Chrome tab is no longer available.");
        }

        var restoredIndex = Math.Max(0, checkpoint.CurrentMessageIndex);
        _engine.RestorePosition(restoredIndex, checkpoint.CompletedMessages, checkpoint.Status);

        return DevelopmentTaskRecoveryResult.Restored(
            monitorId,
            tabId,
            restoredIndex,
            checkpoint.LastDeliveredMessageIndex,
            checkpoint.LastDeliveredMessageFingerprint);
    }
}

public sealed record DevelopmentTaskRecoveryResult(
    bool Success,
    string? MonitorId,
    string? TabId,
    int MessageIndex,
    int LastDeliveredMessageIndex,
    string? LastDeliveredMessageFingerprint,
    string? Reason)
{
    public static DevelopmentTaskRecoveryResult Restored(
        string monitorId,
        string tabId,
        int messageIndex,
        int lastDeliveredMessageIndex,
        string? fingerprint) => new(true, monitorId, tabId, messageIndex, lastDeliveredMessageIndex, fingerprint, null);

    public static DevelopmentTaskRecoveryResult Rejected(string reason) => new(false, null, null, 0, -1, null, reason);
}
