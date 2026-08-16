from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}: found {count}\n--- needle ---\n{old[:500]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


# 1) Deterministic continuation packet: carry an explicit confirmed checkpoint for every fresh-chat handoff.
handoff_path = ROOT / "src/GPTDeskTop/Services/ConversationHandoffService.cs"
handoff_path.write_text(r'''using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Builds a bounded, deterministic continuation message whenever a monitor must move
/// to a fresh ChatGPT conversation. The packet carries the latest confirmed work state,
/// not only the error/limit that triggered the handoff.
/// </summary>
public sealed class ConversationHandoffService
{
    private readonly LocalDatabase _database;

    public ConversationHandoffService(LocalDatabase database) => _database = database;

    public async Task<string> BuildAsync(
        SavedMonitor monitor,
        string triggerResponse,
        ChromeTab previousTab,
        CancellationToken cancellationToken = default)
    {
        if (await _database.GetSettingAsync("HandoffEnabled", cancellationToken) != "1")
            return string.Empty;

        var maxChars = await _database.GetIntSettingAsync("HandoffMaxChars", 7000, 1500, 20000, cancellationToken);
        var logs = await _database.GetRecentLogsForMonitorAsync(monitor.Id, 16, cancellationToken);
        var lastConfirmedInbound = logs.LastOrDefault(log =>
            string.Equals(log.Direction, "Inbound", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Deferred", StringComparison.OrdinalIgnoreCase));
        var lastConfirmedOutbound = logs.LastOrDefault(log =>
            string.Equals(log.Direction, "Outbound", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase)
            && !log.Status.Contains("Deferred", StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder(maxChars + 512);
        builder.AppendLine("هذه رسالة استمرارية من محادثة ChatGPT سابقة. اعتبر المهمة نفسها مستمرة ولا تبدأ من الصفر.");
        builder.AppendLine("تابع من آخر نقطة مؤكدة أدناه، ولا تكرر ما تم إنجازه. لا تدّعي امتلاك سياق غير موجود في هذه الرسالة.");
        builder.AppendLine($"Monitor: #{monitor.Id} | Previous chat: {previousTab.Title} | Source conversation: {monitor.Url} | Continuation: {monitor.RotationCount + 1}");
        builder.AppendLine();
        builder.AppendLine("نقطة الاستكمال المؤكدة:");
        builder.AppendLine(lastConfirmedInbound is null
            ? "[لا يوجد رد Inbound مؤكد في السجل القريب؛ استخدم السجل المنقول أدناه لتحديد آخر نقطة مؤكدة.]"
            : Trim(lastConfirmedInbound.Response, Math.Min(2200, maxChars / 3)));
        builder.AppendLine();
        builder.AppendLine("آخر طلب/تعليمات Outbound مؤكدة قبل الانتقال:");
        builder.AppendLine(lastConfirmedOutbound is null
            ? "[غير متاح]"
            : Trim(lastConfirmedOutbound.Prompt, Math.Min(1400, maxChars / 4)));
        builder.AppendLine();
        builder.AppendLine("سبب الانتقال / آخر حالة ظهرت:");
        builder.AppendLine(Trim(triggerResponse, Math.Min(1200, maxChars / 5)));
        builder.AppendLine();
        builder.AppendLine("السجل القريب للمحادثة:");

        foreach (var log in logs)
        {
            if (log.Direction == "System" &&
                (log.Status.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
                 || log.Status.Contains("HandoffCheckpoint", StringComparison.OrdinalIgnoreCase)))
                continue;

            var content = !string.IsNullOrWhiteSpace(log.Response) ? log.Response : log.Prompt;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            builder.Append('[').Append(log.Direction).Append('/').Append(log.Status).Append("] ");
            builder.AppendLine(Trim(content, 800));
        }

        builder.AppendLine();
        builder.AppendLine("تعليمات الاستمرارية: حافظ على الهدف والقرارات السابقة، واستمر في تنفيذ الخطوة التالية من نقطة الاستكمال المؤكدة. إذا كان العمل برمجيًا، لا تعيد تصميم أو تنفيذ ما ثبت أنه اكتمل.");
        builder.AppendLine("تعليمات السلامة: لا تحاول تجاوز حدود الاستخدام أو التحايل على قيود الخدمة. إذا ظهر خطأ أو حد جديد، تعامل معه وفق الرسالة الفعلية وبشكل محافظ.");

        var result = builder.ToString();
        return result.Length <= maxChars
            ? result
            : result[..maxChars] + "\n[تم اختصار السياق المنقول للحفاظ على حجم آمن للرسالة.]";
    }

    private static string Trim(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "[empty]";

        value = value.Trim();
        return value.Length <= maxChars
            ? value
            : value[..Math.Max(1, maxChars - 80)] + "\n[…truncated…]";
    }
}
''', encoding="utf-8")


# 2) Persist a handoff transaction across CDP/session/app restarts without a schema migration.
checkpoint_path = ROOT / "src/GPTDeskTop/Services/ConversationHandoffCheckpointStore.cs"
checkpoint_path.write_text(r'''using System.Text.Json;
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
''', encoding="utf-8")


monitor_path = ROOT / "src/GPTDeskTop/Services/ChatGptMonitorService.cs"

# Resume a previously accepted-but-uncommitted fresh-chat handoff before reading/sending on the old chat.
replace_once(
    monitor_path,
    '''                    var prefix = $"[{monitor.Title}]";\n                    using var pollFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url);''',
    '''                    var prefix = $"[{monitor.Title}]";\n                    var pendingHandoffTab = await TryResumePendingConversationHandoffAsync(monitor, tab, cancellationToken);\n                    if (pendingHandoffTab is not null)\n                    {\n                        var previousTab = tab;\n                        tab = pendingHandoffTab;\n                        lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;\n                        if (!string.Equals(previousTab.Id, tab.Id, StringComparison.Ordinal))\n                        {\n                            try { await _chrome.CloseTabAsync(previousTab, cancellationToken); }\n                            catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Pending handoff source-tab close was deferred: {closeEx.Message}"); }\n                        }\n                        Activity?.Invoke(monitor.Id, $"{prefix} Persisted handoff checkpoint committed. Monitoring resumes on the fresh conversation without another continuation send.");\n                        continue;\n                    }\n                    using var pollFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url);''')

# Context-limit rotation: persist checkpoint and scope delivery to the new target, never the old conversation ambient scope.
replace_once(
    monitor_path,
    '''                        var oldTab = tab; var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken); await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken); await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken); await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n                        var handoffService = new ConversationHandoffService(_database); var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken); var startMessage = string.IsNullOrWhiteSpace(handoffMessage) ? (string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage) : handoffMessage; var sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);''',
    '''                        var oldTab = tab;\n                        var handoffService = new ConversationHandoffService(_database);\n                        var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken);\n                        var startMessage = string.IsNullOrWhiteSpace(handoffMessage) ? (string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage) : handoffMessage;\n                        await ConversationHandoffCheckpointStore.PrepareAsync(_database, monitor, oldTab, "ConversationContextLimit", startMessage, text, "RotatedToNewChat", "RotationStartSent", "RotationHandoffCommitDeferred", incrementRotationCount: true, recordRotation: true, cancellationToken);\n                        await _database.AddLogAsync("System", startMessage, text, "HandoffCheckpointPrepared", monitor.Id, oldTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n                        await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(_database, monitor.Id, newTab, cancellationToken);\n                        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);\n                        await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken);\n                        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n                        bool sent;\n                        using (RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url))\n                            sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);\n                        if (sent) await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(_database, monitor.Id, newTab, cancellationToken);''')

replace_once(
    monitor_path,
    '''                            await _database.AddLogAsync("System", startMessage, text, "RotationHandoffDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred rotation tab close failed transiently: {closeEx.Message}"); }''',
    '''                            await _database.AddLogAsync("System", startMessage, text, "RotationHandoffDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred rotation tab close failed transiently: {closeEx.Message}"); }''')

# Delivery-timeout recovery now carries the actual work checkpoint, not a bare "كمل".
replace_once(
    monitor_path,
    '''                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat..."); var recoveryMessage = await _database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل"; var oldTab = tab; var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken); await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken); await ApplyModelRouteAsync(monitor, newTab, recovery: true, contextRotation: false, cancellationToken); await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken); var sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);''',
    '''                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat...");\n                        var oldTab = tab;\n                        var fallbackRecoveryMessage = await _database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل";\n                        var handoffService = new ConversationHandoffService(_database);\n                        var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken);\n                        var recoveryMessage = string.IsNullOrWhiteSpace(handoffMessage) ? fallbackRecoveryMessage : handoffMessage;\n                        await ConversationHandoffCheckpointStore.PrepareAsync(_database, monitor, oldTab, "DeliveryTimeout", recoveryMessage, text, "RecoveredToNewChat", "RecoverySent", "RecoveryHandoffCommitDeferred", incrementRotationCount: false, recordRotation: false, cancellationToken);\n                        await _database.AddLogAsync("System", recoveryMessage, text, "HandoffCheckpointPrepared", monitor.Id, oldTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n                        await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(_database, monitor.Id, newTab, cancellationToken);\n                        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);\n                        await ApplyModelRouteAsync(monitor, newTab, recovery: true, contextRotation: false, cancellationToken);\n                        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n                        bool sent;\n                        using (RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url))\n                            sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);\n                        if (sent) await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(_database, monitor.Id, newTab, cancellationToken);''')

replace_once(
    monitor_path,
    '''                            await _database.AddLogAsync("System", recoveryMessage, text, "RecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred recovery tab close failed transiently: {closeEx.Message}"); }''',
    '''                            await _database.AddLogAsync("System", recoveryMessage, text, "RecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred recovery tab close failed transiently: {closeEx.Message}"); }''')

replace_once(
    monitor_path,
    '''                            RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "deferred", "handoff-commit-unverified", tabId: newTab.Id, conversationRef: newTab.Url);\n                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;''',
    '''                            RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "deferred", "handoff-commit-unverified", tabId: newTab.Id, conversationRef: newTab.Url);\n                            Activity?.Invoke(monitor.Id, $"{prefix} Accepted recovery handoff is checkpointed; the same source error will not open another chat while commit reconciliation continues.");\n                            lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;''')

# Generic rendered errors use the same full checkpoint packet and transaction persistence.
replace_once(
    monitor_path,
    '''                        var recoveryMessage = await _database.GetSettingAsync("ChatGptErrorContinuationMessage", cancellationToken)\n                            ?? "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه.";\n                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT error saved. Opening a fresh chat and continuing under the same Monitor ID...");\n                        var oldTab = tab;\n                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);''',
    '''                        var fallbackRecoveryMessage = await _database.GetSettingAsync("ChatGptErrorContinuationMessage", cancellationToken)\n                            ?? "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه.";\n                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT error saved. Opening a fresh chat and continuing under the same Monitor ID...");\n                        var oldTab = tab;\n                        var handoffService = new ConversationHandoffService(_database);\n                        var handoffMessage = await handoffService.BuildAsync(monitor, text, oldTab, cancellationToken);\n                        var recoveryMessage = string.IsNullOrWhiteSpace(handoffMessage) ? fallbackRecoveryMessage : handoffMessage;\n                        await ConversationHandoffCheckpointStore.PrepareAsync(_database, monitor, oldTab, "ChatGptError", recoveryMessage, text, "RecoveredFromChatGptError", "ChatGptErrorContinuationSent", "ChatGptErrorHandoffCommitDeferred", incrementRotationCount: false, recordRotation: false, cancellationToken);\n                        await _database.AddLogAsync("System", recoveryMessage, text, "HandoffCheckpointPrepared", monitor.Id, oldTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n                        await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(_database, monitor.Id, newTab, cancellationToken);''')

replace_once(
    monitor_path,
    '''                        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n                        var sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);\n                        if (!sent)''',
    '''                        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n                        bool sent;\n                        using (RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url))\n                            sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);\n                        if (sent) await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(_database, monitor.Id, newTab, cancellationToken);\n                        if (!sent)''')

replace_once(
    monitor_path,
    '''                            await _database.AddLogAsync("System", recoveryMessage, text, "ChatGptErrorRecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);\n                            HistoryChanged?.Invoke();\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); }''',
    '''                            await _database.AddLogAsync("System", recoveryMessage, text, "ChatGptErrorRecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);\n                            HistoryChanged?.Invoke();\n                            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); }''')

replace_once(
    monitor_path,
    '''                        if (committedRecoveryTab is null)\n                        {\n                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;''',
    '''                        if (committedRecoveryTab is null)\n                        {\n                            Activity?.Invoke(monitor.Id, $"{prefix} Accepted error-recovery handoff is checkpointed; commit reconciliation will resume it without another continuation send.");\n                            lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;''')

# Handoff commit records the stable new URL before the SQLite ownership transaction, then clears only after commit.
replace_once(
    monitor_path,
    '''        if (ChatGptConversationIdentity.IsSame(monitor.Url, stableTab.Url))\n        {''',
    '''        await ConversationHandoffCheckpointStore.MarkTargetResolvedAsync(_database, monitor.Id, stableTab, cancellationToken);\n\n        if (ChatGptConversationIdentity.IsSame(monitor.Url, stableTab.Url))\n        {''')

replace_once(
    monitor_path,
    '''            monitor.Url = committed.NewUrl;\n            monitor.RotationCount = committed.RotationCount;\n            HistoryChanged?.Invoke();\n            return stableTab;''',
    '''            monitor.Url = committed.NewUrl;\n            monitor.RotationCount = committed.RotationCount;\n            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n            HistoryChanged?.Invoke();\n            return stableTab;''')

# Do not destroy an accepted handoff target just because stable identity/commit is temporarily unavailable.
replace_once(
    monitor_path,
    '''            HistoryChanged?.Invoke();\n            try { await _chrome.CloseTabAsync(openedTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff tab close failed transiently: {closeEx.Message}"); }\n            return null;''',
    '''            HistoryChanged?.Invoke();\n            Activity?.Invoke(monitor.Id, "Accepted handoff target is kept open because its stable conversation identity is still pending; persisted checkpoint reconciliation will retry without re-sending.");\n            return null;''')

replace_once(
    monitor_path,
    '''            HistoryChanged?.Invoke();\n            try { await _chrome.CloseTabAsync(stableTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff conflict tab close failed transiently: {closeEx.Message}"); }\n            return null;''',
    '''            HistoryChanged?.Invoke();\n            Activity?.Invoke(monitor.Id, "Accepted handoff target remains available for persisted reconciliation; no duplicate continuation will be sent.");\n            return null;''')

# Proactive message-count rotation also carries the full old-chat checkpoint.
replace_once(
    monitor_path,
    '''        var prefix = $"[{monitor.Title}]";\n        var startMessage = string.IsNullOrWhiteSpace(configuredStartMessage) ? "كمل" : configuredStartMessage.Trim();\n        Activity?.Invoke(monitor.Id, $"{prefix} Assistant count {assistantCount} reached threshold {threshold}. Opening a new ChatGPT conversation...");''',
    '''        var prefix = $"[{monitor.Title}]";\n        var fallbackStartMessage = string.IsNullOrWhiteSpace(configuredStartMessage) ? "كمل" : configuredStartMessage.Trim();\n        var handoffService = new ConversationHandoffService(_database);\n        var handoffMessage = await handoffService.BuildAsync(monitor, triggerText, oldTab, cancellationToken);\n        var startMessage = string.IsNullOrWhiteSpace(handoffMessage) ? fallbackStartMessage : handoffMessage;\n        Activity?.Invoke(monitor.Id, $"{prefix} Assistant count {assistantCount} reached threshold {threshold}. Opening a new ChatGPT conversation...");\n        await ConversationHandoffCheckpointStore.PrepareAsync(_database, monitor, oldTab, "AssistantMessageCount", startMessage, triggerText, "RotatedByMessageCount", "MessageCountRotationStartSent", "MessageCountRotationCommitDeferred", incrementRotationCount: true, recordRotation: true, cancellationToken);\n        await _database.AddLogAsync("System", startMessage, triggerText, "HandoffCheckpointPrepared", monitor.Id, oldTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();''')

replace_once(
    monitor_path,
    '''        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);''',
    '''        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n        await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(_database, monitor.Id, newTab, cancellationToken);\n        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);''')

replace_once(
    monitor_path,
    '''        var sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);\n        if (!sent)''',
    '''        bool sent;\n        using (RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url))\n            sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);\n        if (sent) await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(_database, monitor.Id, newTab, cancellationToken);\n        if (!sent)''')

replace_once(
    monitor_path,
    '''            await _database.AddLogAsync("System", startMessage, triggerText, "MessageCountRotationDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n            try { await _chrome.CloseTabAsync(newTab, cancellationToken); }''',
    '''            await _database.AddLogAsync("System", startMessage, triggerText, "MessageCountRotationDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke();\n            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n            try { await _chrome.CloseTabAsync(newTab, cancellationToken); }''')

# Add the in-process checkpoint reconciliation helper just before message-count rotation.
replace_once(
    monitor_path,
    '''    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)''',
    '''    private async Task<ChromeTab?> TryResumePendingConversationHandoffAsync(SavedMonitor monitor, ChromeTab currentTab, CancellationToken cancellationToken)\n    {\n        try\n        {\n            var recovered = await ConversationHandoffCheckpointStore.TryCompleteAcceptedAsync(_chrome, _database, monitor, cancellationToken);\n            if (recovered is not null)\n            {\n                HistoryChanged?.Invoke();\n                return recovered;\n            }\n        }\n        catch (Exception ex) when (IsTransientChromeException(ex))\n        {\n            Activity?.Invoke(monitor.Id, $"Pending conversation handoff reconciliation is waiting for Chrome/CDP recovery: {ex.GetType().Name}.");\n        }\n        catch (InvalidOperationException ex)\n        {\n            Activity?.Invoke(monitor.Id, $"Pending conversation handoff reconciliation is deferred: {ex.Message}");\n        }\n\n        return null;\n    }\n\n    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)''')

# Startup CDP failures no longer terminate the worker after three attempts.
replace_once(
    monitor_path,
    '''    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)\n    { Exception? last = null; for (var attempt = 1; attempt <= 3; attempt++) { try { return await _chrome.GetChatStateAsync(tab, cancellationToken); } catch (Exception ex) when (IsTransientChromeException(ex)) { last = ex; Activity?.Invoke(monitorId, $"Initial Chrome/CDP connection retry {attempt}/3: {ex.GetType().Name}"); await Task.Delay(500 * attempt, cancellationToken); } } throw last ?? new InvalidOperationException("Unable to read the ChatGPT tab state."); }''',
    '''    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)\n    {\n        for (var attempt = 1; ; attempt++)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            try { return await _chrome.GetChatStateAsync(tab, cancellationToken); }\n            catch (Exception ex) when (IsTransientChromeException(ex))\n            {\n                if (attempt <= 3 || attempt % 12 == 0)\n                    Activity?.Invoke(monitorId, $"Chrome/CDP connection retry {attempt}: {ex.GetType().Name}. Monitor remains active and will keep self-healing.");\n                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 500 * attempt)), cancellationToken);\n            }\n        }\n    }''')


# 3) App restart: reconcile an accepted pending handoff before normal saved-tab recovery and suppress duplicate startup follow-up.
last_working_path = ROOT / "src/GPTDeskTop/Services/LastWorkingStateService.cs"
replace_once(
    last_working_path,
    '''                var recovery = await MonitorTabRecoveryService.EnsureMonitorTabAsync(\n                    chrome,\n                    database,\n                    savedMonitor,\n                    sendFollowUpWhenRecreated: true,\n                    cancellationToken).ConfigureAwait(false);''',
    '''                var pendingHandoffTab = await ConversationHandoffCheckpointStore.TryCompleteAcceptedAsync(\n                    chrome,\n                    database,\n                    savedMonitor,\n                    cancellationToken).ConfigureAwait(false);\n                var pendingHandoffCompleted = pendingHandoffTab is not null;\n                var recovery = pendingHandoffCompleted\n                    ? new MonitorTabRecoveryResult(pendingHandoffTab!, Recreated: false, BrowserRestarted: false, FollowUpSent: false)\n                    : await MonitorTabRecoveryService.EnsureMonitorTabAsync(\n                        chrome,\n                        database,\n                        savedMonitor,\n                        sendFollowUpWhenRecreated: true,\n                        cancellationToken).ConfigureAwait(false);''')

replace_once(
    last_working_path,
    '''                var startupFollowUpAttempted = recovery.Recreated || !string.IsNullOrWhiteSpace(savedMonitor.AutoReply);\n                var startupFollowUpSent = recovery.FollowUpSent;\n                if (!recovery.Recreated && !string.IsNullOrWhiteSpace(savedMonitor.AutoReply))''',
    '''                var startupFollowUpAttempted = !pendingHandoffCompleted && (recovery.Recreated || !string.IsNullOrWhiteSpace(savedMonitor.AutoReply));\n                var startupFollowUpSent = recovery.FollowUpSent;\n                if (!pendingHandoffCompleted && !recovery.Recreated && !string.IsNullOrWhiteSpace(savedMonitor.AutoReply))''')

replace_once(
    last_working_path,
    '''                    var reason = recovery.Recreated\n                        ? startupFollowUpSent''',
    '''                    var reason = pendingHandoffCompleted\n                        ? "PendingHandoffRecoveredWithoutDuplicateFollowUp"\n                        : recovery.Recreated\n                        ? startupFollowUpSent''')


# 4) Regression contract for the exact field failure: old conversation scope + bare continuation + lost commit.
test_path = ROOT / "tests/GPTDeskTop.RuntimeTests/HandoffContinuityCheckpointRegressionTests.cs"
test_path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class HandoffContinuityCheckpointRegressionTests
{
    private static string Source(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            parts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void DeliveryTimeoutFreshChatCarriesConfirmedWorkCheckpointAndNewTargetScope()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = source.IndexOf("if (isError && IsDeliveryTimeout(text))", StringComparison.Ordinal);
        var end = source.IndexOf("if (isError)", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = source[start..end];

        Assert.Contains("new ConversationHandoffService(_database)", block, StringComparison.Ordinal);
        Assert.Contains("HandoffCheckpointPrepared", block, StringComparison.Ordinal);
        Assert.Contains("ConversationHandoffCheckpointStore.MarkTargetCreatedAsync", block, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url)", block, StringComparison.Ordinal);
        Assert.Contains("ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync", block, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedDeliveryTimeoutCommitFailureKeepsSourceHandledUntilCheckpointReconciles()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var recoveryStart = source.IndexOf("if (isError && IsDeliveryTimeout(text))", StringComparison.Ordinal);
        var commitFailure = source.IndexOf("if (committedRecoveryTab is null)", recoveryStart, StringComparison.Ordinal);
        var commitFailureEnd = source.IndexOf("continue;", commitFailure, StringComparison.Ordinal);
        Assert.True(commitFailure > recoveryStart && commitFailureEnd > commitFailure);
        var block = source[commitFailure..commitFailureEnd];

        Assert.Contains("lastHandledText = text", block, StringComparison.Ordinal);
        Assert.DoesNotContain("lastHandledText = string.Empty", block, StringComparison.Ordinal);
        Assert.Contains("checkpointed", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandoffPacketContainsExplicitConfirmedCheckpoint()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ConversationHandoffService.cs");
        Assert.Contains("نقطة الاستكمال المؤكدة", source, StringComparison.Ordinal);
        Assert.Contains("آخر طلب/تعليمات Outbound مؤكدة", source, StringComparison.Ordinal);
        Assert.Contains("Source conversation", source, StringComparison.Ordinal);
        Assert.Contains("HandoffCheckpoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedPendingHandoffIsRecoveredBeforeStartupFollowUp()
    {
        var source = Source("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        var pending = source.IndexOf("pendingHandoffCompleted", StringComparison.Ordinal);
        var followUp = source.IndexOf("SendExistingTabStartupFollowUpAsync", pending, StringComparison.Ordinal);
        Assert.True(pending >= 0 && followUp > pending);
        Assert.Contains("!pendingHandoffCompleted", source[pending..followUp], StringComparison.Ordinal);
        Assert.Contains("PendingHandoffRecoveredWithoutDuplicateFollowUp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialCdpRecoveryRemainsActiveInsteadOfStoppingAfterThreeAttempts()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = source.IndexOf("private async Task<ChatPageState> GetChatStateWithRetryAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool IsTransientChromeException", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("for (var attempt = 1; ; attempt++)", method, StringComparison.Ordinal);
        Assert.Contains("Monitor remains active and will keep self-healing", method, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt <= 3", method, StringComparison.Ordinal);
    }
}
''', encoding="utf-8")

print("handoff continuity checkpoint patch applied")
