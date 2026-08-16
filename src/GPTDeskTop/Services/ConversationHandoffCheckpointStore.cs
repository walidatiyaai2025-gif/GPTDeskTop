using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ConversationHandoffCheckpoint(
    long MonitorId,
    string SourceUrl,
    string SourceTabId,
    string SourceTitle,
    string TargetTabId,
    string TargetTitle,
    string TargetUrl,
    string RotationTrigger,
    string StartMessage,
    string TriggerResponse,
    string SuccessStatus,
    string OutboundStatus,
    string ConflictStatus,
    bool IncrementRotationCount,
    bool RecordRotation,
    string Stage,
    DateTimeOffset UpdatedUtc);

public static class ConversationHandoffCheckpointStore
{
    private const string KeyPrefix = "Runtime.PendingConversationHandoff.";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task PrepareAsync(
        LocalDatabase database,
        SavedMonitor monitor,
        ChromeTab sourceTab,
        string rotationTrigger,
        string startMessage,
        string triggerResponse,
        string successStatus,
        string outboundStatus,
        string conflictStatus,
        bool incrementRotationCount,
        bool recordRotation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(sourceTab);

        var checkpoint = new ConversationHandoffCheckpoint(
            monitor.Id,
            monitor.Url ?? string.Empty,
            sourceTab.Id ?? string.Empty,
            sourceTab.Title ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            rotationTrigger ?? string.Empty,
            startMessage ?? string.Empty,
            triggerResponse ?? string.Empty,
            successStatus ?? string.Empty,
            outboundStatus ?? string.Empty,
            conflictStatus ?? string.Empty,
            incrementRotationCount,
            recordRotation,
            "Prepared",
            DateTimeOffset.UtcNow);

        await SaveAsync(database, checkpoint, cancellationToken).ConfigureAwait(false);
    }

    public static Task MarkTargetCreatedAsync(
        LocalDatabase database,
        long monitorId,
        ChromeTab targetTab,
        CancellationToken cancellationToken = default)
        => MutateAsync(database, monitorId, checkpoint => checkpoint with
        {
            TargetTabId = targetTab.Id ?? string.Empty,
            TargetTitle = targetTab.Title ?? string.Empty,
            TargetUrl = RuntimeHealthPresentation.IsChatGptConversationUrl(targetTab.Url) ? targetTab.Url : checkpoint.TargetUrl,
            Stage = "TargetCreated",
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

    public static Task MarkDeliveryAcceptedAsync(
        LocalDatabase database,
        long monitorId,
        ChromeTab targetTab,
        CancellationToken cancellationToken = default)
        => MutateAsync(database, monitorId, checkpoint => checkpoint with
        {
            TargetTabId = targetTab.Id ?? checkpoint.TargetTabId,
            TargetTitle = targetTab.Title ?? checkpoint.TargetTitle,
            TargetUrl = RuntimeHealthPresentation.IsChatGptConversationUrl(targetTab.Url) ? targetTab.Url : checkpoint.TargetUrl,
            Stage = "DeliveryAccepted",
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

    public static Task MarkTargetResolvedAsync(
        LocalDatabase database,
        long monitorId,
        ChromeTab targetTab,
        CancellationToken cancellationToken = default)
        => MutateAsync(database, monitorId, checkpoint => checkpoint with
        {
            TargetTabId = targetTab.Id ?? checkpoint.TargetTabId,
            TargetTitle = targetTab.Title ?? checkpoint.TargetTitle,
            TargetUrl = targetTab.Url ?? checkpoint.TargetUrl,
            Stage = "TargetResolved",
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

    public static async Task<ConversationHandoffCheckpoint?> LoadAsync(
        LocalDatabase database,
        long monitorId,
        CancellationToken cancellationToken = default)
    {
        var raw = await database.GetSettingAsync(Key(monitorId), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try { return JsonSerializer.Deserialize<ConversationHandoffCheckpoint>(raw); }
        catch (JsonException) { return null; }
    }

    public static async Task ClearAsync(
        LocalDatabase database,
        long monitorId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await database.SetSettingAsync(Key(monitorId), string.Empty, cancellationToken).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    public static async Task<ChromeTab?> TryCompleteAcceptedAsync(
        ChromeDevToolsService chrome,
        LocalDatabase database,
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await LoadAsync(database, monitor.Id, cancellationToken).ConfigureAwait(false);
        if (checkpoint is null || checkpoint.Stage is not ("DeliveryAccepted" or "TargetResolved"))
            return null;

        var tabs = await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        ChromeTab? target = null;
        if (RuntimeHealthPresentation.IsChatGptConversationUrl(checkpoint.TargetUrl))
        {
            target = tabs.FirstOrDefault(tab =>
                RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)
                && ChatGptConversationIdentity.IsSame(checkpoint.TargetUrl, tab.Url));
        }

        target ??= tabs.FirstOrDefault(tab => string.Equals(tab.Id, checkpoint.TargetTabId, StringComparison.Ordinal));
        if (target is not null && RuntimeHealthPresentation.IsChatGptConversationUrl(target.Url))
        {
            await MarkTargetResolvedAsync(database, monitor.Id, target, cancellationToken).ConfigureAwait(false);
            checkpoint = (await LoadAsync(database, monitor.Id, cancellationToken).ConfigureAwait(false)) ?? checkpoint;
        }

        if (target is null && RuntimeHealthPresentation.IsChatGptConversationUrl(checkpoint.TargetUrl))
            target = await chrome.CreateTabAsync(checkpoint.TargetUrl, cancellationToken).ConfigureAwait(false);

        if (target is null || !RuntimeHealthPresentation.IsChatGptConversationUrl(target.Url))
            return null;

        if (!ChatGptConversationIdentity.IsSame(monitor.Url, checkpoint.SourceUrl))
        {
            if (ChatGptConversationIdentity.IsSame(monitor.Url, target.Url))
            {
                await ClearAsync(database, monitor.Id, cancellationToken).ConfigureAwait(false);
                return target;
            }
            return null;
        }

        var committed = await database.CommitMonitorConversationHandoffAsync(
            monitor.Id,
            checkpoint.SourceUrl,
            target.Id,
            target.Title,
            target.Url,
            checkpoint.IncrementRotationCount,
            checkpoint.RecordRotation,
            checkpoint.SourceTabId,
            checkpoint.RotationTrigger,
            checkpoint.StartMessage,
            checkpoint.TriggerResponse,
            checkpoint.SuccessStatus,
            checkpoint.OutboundStatus,
            cancellationToken).ConfigureAwait(false);

        target.Title = committed.Title;
        monitor.TabId = target.Id;
        monitor.Title = committed.Title;
        monitor.Url = committed.NewUrl;
        monitor.RotationCount = committed.RotationCount;
        await ClearAsync(database, monitor.Id, cancellationToken).ConfigureAwait(false);
        return target;
    }

    private static async Task SaveAsync(
        LocalDatabase database,
        ConversationHandoffCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await database.SetSettingAsync(
                Key(checkpoint.MonitorId),
                JsonSerializer.Serialize(checkpoint),
                cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static async Task MutateAsync(
        LocalDatabase database,
        long monitorId,
        Func<ConversationHandoffCheckpoint, ConversationHandoffCheckpoint> mutation,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var raw = await database.GetSettingAsync(Key(monitorId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw)) return;
            ConversationHandoffCheckpoint? checkpoint;
            try { checkpoint = JsonSerializer.Deserialize<ConversationHandoffCheckpoint>(raw); }
            catch (JsonException) { return; }
            if (checkpoint is null) return;
            await database.SetSettingAsync(
                Key(monitorId),
                JsonSerializer.Serialize(mutation(checkpoint)),
                cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static string Key(long monitorId) => $"{KeyPrefix}{monitorId}";
}
