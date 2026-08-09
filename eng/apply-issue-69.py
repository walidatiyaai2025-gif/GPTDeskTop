from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''public sealed record MonitorConversationRebindDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl);\n''',
    '''public sealed record MonitorConversationRebindDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl);\npublic sealed record MonitorConversationHandoffDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl, int RotationCount, string Title);\n''')

replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''    private static void ClampMonitorSettings(SavedMonitor monitor)\n''',
    r'''    public async Task<MonitorConversationHandoffDatabaseResult> CommitMonitorConversationHandoffAsync(
        long monitorId,
        string expectedCurrentUrl,
        string targetTabId,
        string targetTitle,
        string targetUrl,
        bool incrementRotationCount,
        bool recordRotation,
        string oldTabId,
        string rotationTrigger,
        string startMessage,
        string triggerResponse,
        string successStatus,
        string outboundStatus,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(expectedCurrentUrl))
            throw new InvalidOperationException("The current monitor conversation identity is required for handoff.");
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The new conversation Chrome target ID is required for handoff.");
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new InvalidOperationException("The new stable conversation URL is required for handoff.");
        if (string.Equals(expectedCurrentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Intentional handoff requires a different target conversation.");

        targetTitle ??= string.Empty;
        oldTabId ??= string.Empty;
        rotationTrigger ??= string.Empty;
        startMessage ??= string.Empty;
        triggerResponse ??= string.Empty;
        successStatus ??= string.Empty;
        outboundStatus ??= string.Empty;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            string currentUrl;
            string currentTitle;
            int currentRotationCount;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url, Title, RotationCount FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                await using var reader = await load.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");
                currentUrl = reader.GetString(0);
                currentTitle = reader.GetString(1);
                currentRotationCount = reader.GetInt32(2);
            }

            if (!string.Equals(currentUrl, expectedCurrentUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("Saved monitor conversation identity changed before intentional handoff could be committed.");

            await using (var targetOwner = connection.CreateCommand())
            {
                targetOwner.Transaction = transaction;
                targetOwner.CommandText = "SELECT Id FROM SavedMonitors WHERE Id<>$id AND Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                targetOwner.Parameters.AddWithValue("$id", monitorId);
                targetOwner.Parameters.AddWithValue("$url", targetUrl);
                var existing = await targetOwner.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                    throw new InvalidOperationException($"Monitor #{Convert.ToInt64(existing)} already owns the intentional handoff target conversation.");
            }

            var nextRotationCount = incrementRotationCount ? checked(currentRotationCount + 1) : currentRotationCount;
            var appliedTitle = string.IsNullOrWhiteSpace(targetTitle) ? currentTitle : targetTitle;
            var now = DateTime.UtcNow.ToString("O");

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, Url=$url, RotationCount=$rotationCount, UpdatedAt=$updatedAt WHERE Id=$id;";
                update.Parameters.AddWithValue("$id", monitorId);
                update.Parameters.AddWithValue("$tabId", targetTabId);
                update.Parameters.AddWithValue("$title", appliedTitle);
                update.Parameters.AddWithValue("$url", targetUrl);
                update.Parameters.AddWithValue("$rotationCount", nextRotationCount);
                update.Parameters.AddWithValue("$updatedAt", now);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Saved monitor #{monitorId} could not be moved to the new conversation.");
            }

            if (recordRotation)
            {
                await using var rotation = connection.CreateCommand();
                rotation.Transaction = transaction;
                rotation.CommandText = "INSERT INTO ConversationRotations(MonitorId,OldTabId,NewTabId,Trigger,StartMessage,Timestamp) VALUES($m,$o,$n,$t,$s,$ts);";
                rotation.Parameters.AddWithValue("$m", monitorId);
                rotation.Parameters.AddWithValue("$o", oldTabId);
                rotation.Parameters.AddWithValue("$n", targetTabId);
                rotation.Parameters.AddWithValue("$t", rotationTrigger);
                rotation.Parameters.AddWithValue("$s", startMessage);
                rotation.Parameters.AddWithValue("$ts", now);
                await rotation.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var systemLog = connection.CreateCommand())
            {
                systemLog.Transaction = transaction;
                systemLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,'System',$p,$r,$s);";
                systemLog.Parameters.AddWithValue("$ts", now);
                systemLog.Parameters.AddWithValue("$m", monitorId);
                systemLog.Parameters.AddWithValue("$id", targetTabId);
                systemLog.Parameters.AddWithValue("$title", appliedTitle);
                systemLog.Parameters.AddWithValue("$p", startMessage);
                systemLog.Parameters.AddWithValue("$r", triggerResponse);
                systemLog.Parameters.AddWithValue("$s", successStatus);
                await systemLog.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var outboundLog = connection.CreateCommand())
            {
                outboundLog.Transaction = transaction;
                outboundLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,'Outbound',$p,'',$s);";
                outboundLog.Parameters.AddWithValue("$ts", now);
                outboundLog.Parameters.AddWithValue("$m", monitorId);
                outboundLog.Parameters.AddWithValue("$id", targetTabId);
                outboundLog.Parameters.AddWithValue("$title", appliedTitle);
                outboundLog.Parameters.AddWithValue("$p", startMessage);
                outboundLog.Parameters.AddWithValue("$s", outboundStatus);
                await outboundLog.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new MonitorConversationHandoffDatabaseResult(
                monitorId,
                currentUrl,
                targetUrl,
                nextRotationCount,
                appliedTitle);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static void ClampMonitorSettings(SavedMonitor monitor)
''')

# Context-limit rotation: replace broad identity mutation/save/log sequence with guarded handoff.
replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''                        monitor.RotationCount++; monitor.TabId = newTab.Id; monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? $"ChatGPT Chat #{monitor.RotationCount + 1}" : newTab.Title; monitor.Url = newTab.Url; await _database.SaveMonitorAsync(monitor, cancellationToken); await _database.AddConversationRotationAsync(monitor.Id, oldTab.Id, newTab.Id, "ConversationContextLimit", startMessage, cancellationToken); await _database.AddLogAsync("System", startMessage, text, "RotatedToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken); await _database.AddLogAsync("Outbound", startMessage, string.Empty, "RotationStartSent", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); tab = newTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"{prefix} Rotation #{monitor.RotationCount} complete. Monitoring the new ChatGPT conversation under the same Monitor ID.");\n''',
    '''                        var committedTab = await CommitVerifiedConversationHandoffAsync(\n                            monitor, oldTab, newTab, startMessage, text,\n                            rotationTrigger: "ConversationContextLimit",\n                            successStatus: "RotatedToNewChat",\n                            outboundStatus: "RotationStartSent",\n                            conflictStatus: "RotationHandoffCommitDeferred",\n                            incrementRotationCount: true,\n                            recordRotation: true,\n                            cancellationToken);\n                        if (committedTab is null)\n                        {\n                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; lastResponseActivity = DateTimeOffset.UtcNow;\n                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);\n                            continue;\n                        }\n                        tab = committedTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"{prefix} Rotation #{monitor.RotationCount} complete. Monitoring the new ChatGPT conversation under the same Monitor ID.");\n''')

# Delivery-timeout recovery intentional handoff.
replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''                        monitor.TabId = newTab.Id; monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? "Recovered ChatGPT Chat" : newTab.Title; monitor.Url = newTab.Url; await _database.SaveMonitorAsync(monitor, cancellationToken); await _database.AddLogAsync("System", recoveryMessage, text, "RecoveredToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken); await _database.AddLogAsync("Outbound", recoveryMessage, string.Empty, "RecoverySent", monitor.Id, newTab.Id, monitor.Title, cancellationToken); HistoryChanged?.Invoke(); tab = newTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;\n''',
    '''                        var committedRecoveryTab = await CommitVerifiedConversationHandoffAsync(\n                            monitor, oldTab, newTab, recoveryMessage, text,\n                            rotationTrigger: "DeliveryTimeout",\n                            successStatus: "RecoveredToNewChat",\n                            outboundStatus: "RecoverySent",\n                            conflictStatus: "RecoveryHandoffCommitDeferred",\n                            incrementRotationCount: false,\n                            recordRotation: false,\n                            cancellationToken);\n                        if (committedRecoveryTab is null)\n                        {\n                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; lastResponseActivity = DateTimeOffset.UtcNow;\n                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);\n                            continue;\n                        }\n                        tab = committedRecoveryTab; lastHandledText = string.Empty; lastResponseActivity = DateTimeOffset.UtcNow; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;\n''')

# Message-count rotation helper persistence sequence.
replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''        monitor.RotationCount++;\n        monitor.TabId = newTab.Id;\n        monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? $"ChatGPT Chat #{monitor.RotationCount + 1}" : newTab.Title;\n        monitor.Url = newTab.Url;\n        await _database.SaveMonitorAsync(monitor, cancellationToken);\n        await _database.AddConversationRotationAsync(monitor.Id, oldTab.Id, newTab.Id, "AssistantMessageCount", startMessage, cancellationToken);\n        await _database.AddLogAsync("System", startMessage, triggerText, "RotatedByMessageCount", monitor.Id, newTab.Id, monitor.Title, cancellationToken);\n        await _database.AddLogAsync("Outbound", startMessage, string.Empty, "MessageCountRotationStartSent", monitor.Id, newTab.Id, monitor.Title, cancellationToken);\n        HistoryChanged?.Invoke();\n        Activity?.Invoke(monitor.Id, $"{prefix} Message-count rotation #{monitor.RotationCount} complete. Same Monitor ID is now bound to the new conversation.");\n\n        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after message-count rotation: {closeEx.Message}"); }\n        if (monitor.RotationCooldownSeconds > 0)\n            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken);\n\n        return newTab;\n''',
    '''        var committedTab = await CommitVerifiedConversationHandoffAsync(\n            monitor, oldTab, newTab, startMessage, triggerText,\n            rotationTrigger: "AssistantMessageCount",\n            successStatus: "RotatedByMessageCount",\n            outboundStatus: "MessageCountRotationStartSent",\n            conflictStatus: "MessageCountRotationCommitDeferred",\n            incrementRotationCount: true,\n            recordRotation: true,\n            cancellationToken);\n        if (committedTab is null)\n            return null;\n\n        Activity?.Invoke(monitor.Id, $"{prefix} Message-count rotation #{monitor.RotationCount} complete. Same Monitor ID is now bound to the new conversation.");\n\n        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old chat close was deferred after message-count rotation: {closeEx.Message}"); }\n        if (monitor.RotationCooldownSeconds > 0)\n            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600)), cancellationToken);\n\n        return committedTab;\n''')

# Add shared stable-target resolution + transactional commit helper before message-count helper.
replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)\n''',
    r'''    private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync(
        SavedMonitor monitor,
        ChromeTab oldTab,
        ChromeTab openedTab,
        string startMessage,
        string triggerResponse,
        string rotationTrigger,
        string successStatus,
        string outboundStatus,
        string conflictStatus,
        bool incrementRotationCount,
        bool recordRotation,
        CancellationToken cancellationToken)
    {
        var stableTab = await ResolveStableCreatedConversationAsync(monitor.Id, openedTab, cancellationToken);
        if (stableTab is null)
        {
            await _database.AddLogAsync(
                "System",
                startMessage,
                "Verified delivery succeeded, but Chrome did not expose a stable /c/{conversation-id} URL for the new target. The new tab was not claimed.",
                conflictStatus,
                monitor.Id,
                openedTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(openedTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff tab close failed transiently: {closeEx.Message}"); }
            return null;
        }

        if (ChatGptConversationIdentity.IsSame(monitor.Url, stableTab.Url))
        {
            await _database.AddLogAsync(
                "System",
                startMessage,
                "The new handoff target resolved back to the current saved conversation. The target was not claimed.",
                conflictStatus,
                monitor.Id,
                stableTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(stableTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed same-conversation tab close failed transiently: {closeEx.Message}"); }
            return null;
        }

        var expectedUrl = monitor.Url;
        try
        {
            var committed = await _database.CommitMonitorConversationHandoffAsync(
                monitor.Id,
                expectedUrl,
                stableTab.Id,
                stableTab.Title,
                stableTab.Url,
                incrementRotationCount,
                recordRotation,
                oldTab.Id,
                rotationTrigger,
                startMessage,
                triggerResponse,
                successStatus,
                outboundStatus,
                cancellationToken);

            stableTab.Title = committed.Title;
            monitor.TabId = stableTab.Id;
            monitor.Title = committed.Title;
            monitor.Url = committed.NewUrl;
            monitor.RotationCount = committed.RotationCount;
            HistoryChanged?.Invoke();
            return stableTab;
        }
        catch (InvalidOperationException ex)
        {
            Activity?.Invoke(monitor.Id, $"Intentional conversation handoff was not committed: {ex.Message}");
            await _database.AddLogAsync(
                "System",
                startMessage,
                ex.Message,
                conflictStatus,
                monitor.Id,
                stableTab.Id,
                monitor.Title,
                cancellationToken);
            HistoryChanged?.Invoke();
            try { await _chrome.CloseTabAsync(stableTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unclaimed handoff conflict tab close failed transiently: {closeEx.Message}"); }
            return null;
        }
    }

    private async Task<ChromeTab?> ResolveStableCreatedConversationAsync(long monitorId, ChromeTab openedTab, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await _chrome.GetTabsAsync(cancellationToken);
                var current = tabs.FirstOrDefault(tab => string.Equals(tab.Id, openedTab.Id, StringComparison.Ordinal));
                if (current is not null && RuntimeHealthPresentation.IsChatGptConversationUrl(current.Url))
                {
                    Activity?.Invoke(monitorId, $"[{current.Title}] Stable conversation identity resolved after verified new-chat delivery.");
                    return current;
                }
            }
            catch (Exception ex) when (IsTransientChromeException(ex))
            {
                Activity?.Invoke(monitorId, $"Waiting for stable new-chat conversation identity: {ex.GetType().Name}.");
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }

    private async Task<ChromeTab?> RotateByMessageCountAsync(SavedMonitor monitor, ChromeTab oldTab, int assistantCount, int threshold, string triggerText, string configuredStartMessage, CancellationToken cancellationToken)
''')

# Update old source-contract tests to lock the new ordering rather than broad SaveMonitor persistence.
replace_once(
    "tests/GPTDeskTop.RuntimeTests/MessageCountRotationRegressionTests.cs",
    '''        var increment = source.IndexOf("monitor.RotationCount++", helper, StringComparison.Ordinal);\n        var save = source.IndexOf("await _database.SaveMonitorAsync(monitor", increment, StringComparison.Ordinal);\n        var closeOld = source.IndexOf("await _chrome.CloseTabAsync(oldTab", save, StringComparison.Ordinal);\n\n        Assert.True(helper >= 0);\n        Assert.True(verifiedSend > helper);\n        Assert.True(increment > verifiedSend);\n        Assert.True(save > increment);\n        Assert.True(closeOld > save);\n''',
    '''        var commit = source.IndexOf("CommitVerifiedConversationHandoffAsync", verifiedSend, StringComparison.Ordinal);\n        var closeOld = source.IndexOf("await _chrome.CloseTabAsync(oldTab", commit, StringComparison.Ordinal);\n\n        Assert.True(helper >= 0);\n        Assert.True(verifiedSend > helper);\n        Assert.True(commit > verifiedSend);\n        Assert.True(closeOld > commit);\n        Assert.DoesNotContain("await _database.SaveMonitorAsync(monitor", source[helper..closeOld], StringComparison.Ordinal);\n''')

replace_once(
    "tests/GPTDeskTop.RuntimeTests/ChatGptRotationHandoffRegressionTests.cs",
    '''        var successfulRotation = source.IndexOf("monitor.RotationCount++", deferred, StringComparison.Ordinal);\n        var closeOldTab = source.IndexOf("await _chrome.CloseTabAsync(oldTab", successfulRotation, StringComparison.Ordinal);\n''',
    '''        var successfulRotation = source.IndexOf("CommitVerifiedConversationHandoffAsync", deferred, StringComparison.Ordinal);\n        var closeOldTab = source.IndexOf("await _chrome.CloseTabAsync(oldTab", successfulRotation, StringComparison.Ordinal);\n''')

# Real-SQLite handoff transaction coverage.
Path("tests/GPTDeskTop.RuntimeTests/TransactionalConversationHandoffTests.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class TransactionalConversationHandoffTests
{
    [Fact]
    public async Task RotationHandoffPreservesConfigurationAndCommitsIdentityCountRotationAndReceiptsTogether()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var database = new LocalDatabase(path);
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "old-target",
                Title = "Old title",
                Url = "https://chatgpt.com/c/old",
                AutoReply = "keep-config",
                ReplyDelaySeconds = 17,
                TimerSeconds = 9,
                Enabled = false,
                ConversationRotationEnabled = true,
                RotationCount = 4,
                ModelRoutingEnabled = true,
                PreferredModel = "GPT-5",
                FallbackModel = "Auto"
            };
            var monitorId = await database.SaveMonitorAsync(monitor);

            var result = await database.CommitMonitorConversationHandoffAsync(
                monitorId,
                "https://chatgpt.com/c/old",
                "new-target",
                "New title",
                "https://chatgpt.com/c/new",
                incrementRotationCount: true,
                recordRotation: true,
                oldTabId: "old-target",
                rotationTrigger: "AssistantMessageCount",
                startMessage: "continue",
                triggerResponse: "trigger",
                successStatus: "RotatedByMessageCount",
                outboundStatus: "MessageCountRotationStartSent");

            Assert.Equal(5, result.RotationCount);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("new-target", saved.TabId);
            Assert.Equal("New title", saved.Title);
            Assert.Equal("https://chatgpt.com/c/new", saved.Url);
            Assert.Equal(5, saved.RotationCount);
            Assert.Equal("keep-config", saved.AutoReply);
            Assert.Equal(17, saved.ReplyDelaySeconds);
            Assert.Equal(9, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.True(saved.ConversationRotationEnabled);
            Assert.True(saved.ModelRoutingEnabled);
            Assert.Equal("GPT-5", saved.PreferredModel);
            Assert.Equal("Auto", saved.FallbackModel);

            var logs = await database.GetRecentLogsForMonitorAsync(monitorId, 20);
            Assert.Contains(logs, log => log.Status == "RotatedByMessageCount" && log.TabId == "new-target");
            Assert.Contains(logs, log => log.Status == "MessageCountRotationStartSent" && log.Direction == "Outbound");
            Assert.Equal(1, await CountRotationRowsAsync(path, monitorId));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task TimeoutHandoffMovesIdentityWithoutIncrementingRotationOrWritingRotationRow()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var database = new LocalDatabase(path);
            await database.InitializeAsync();
            var monitorId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "old",
                Title = "Old",
                Url = "https://chatgpt.com/c/timeout-old",
                RotationCount = 7
            });

            var result = await database.CommitMonitorConversationHandoffAsync(
                monitorId,
                "https://chatgpt.com/c/timeout-old",
                "recovery-target",
                "Recovered",
                "https://chatgpt.com/c/timeout-new",
                incrementRotationCount: false,
                recordRotation: false,
                oldTabId: "old",
                rotationTrigger: "DeliveryTimeout",
                startMessage: "resume",
                triggerResponse: "Message delivery timed out",
                successStatus: "RecoveredToNewChat",
                outboundStatus: "RecoverySent");

            Assert.Equal(7, result.RotationCount);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("https://chatgpt.com/c/timeout-new", saved.Url);
            Assert.Equal(7, saved.RotationCount);
            Assert.Equal(0, await CountRotationRowsAsync(path, monitorId));
            var logs = await database.GetRecentLogsForMonitorAsync(monitorId, 20);
            Assert.Contains(logs, log => log.Status == "RecoveredToNewChat");
            Assert.Contains(logs, log => log.Status == "RecoverySent");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task HandoffCompetingWithRegistrationCannotCreateDuplicateTargetOwner()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var handoffDb = new LocalDatabase(path);
            var registrationDb = new LocalDatabase(path);
            await handoffDb.InitializeAsync();
            var monitorId = await handoffDb.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "old",
                Title = "Old",
                Url = "https://chatgpt.com/c/handoff-source"
            });
            const string targetUrl = "https://chatgpt.com/c/handoff-target";
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var handoffTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await handoffDb.CommitMonitorConversationHandoffAsync(
                        monitorId, "https://chatgpt.com/c/handoff-source", "new-target", "New", targetUrl,
                        true, true, "old", "AssistantMessageCount", "continue", "trigger", "Rotated", "Sent");
                    return (Succeeded: true, Error: (Exception?)null);
                }
                catch (Exception ex) { return (Succeeded: false, Error: ex); }
            });

            var registrationTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var result = await registrationDb.RegisterMonitorIfConversationAvailableAsync(new SavedMonitor
                    {
                        TabId = "registration",
                        Title = "Registration",
                        Url = targetUrl
                    });
                    return (Succeeded: true, Result: result, Error: (Exception?)null);
                }
                catch (Exception ex) { return (Succeeded: false, Result: (MonitorRegistrationResult?)null, Error: ex); }
            });

            start.SetResult();
            await Task.WhenAll(handoffTask, registrationTask);

            var monitors = await handoffDb.GetSavedMonitorsAsync();
            Assert.Single(monitors, item => string.Equals(item.Url, targetUrl, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                monitors.GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase),
                group => group.Count() > 1 && string.Equals(group.Key, targetUrl, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CompetingHandoffsCannotClaimSameTargetConversation()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var firstDb = new LocalDatabase(path);
            var secondDb = new LocalDatabase(path);
            await firstDb.InitializeAsync();
            var firstId = await firstDb.SaveMonitorAsync(new SavedMonitor { TabId = "a", Title = "A", Url = "https://chatgpt.com/c/source-a" });
            var secondId = await firstDb.SaveMonitorAsync(new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/source-b" });
            const string targetUrl = "https://chatgpt.com/c/shared-handoff-target";
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<(bool Succeeded, Exception? Error)> RunAsync(LocalDatabase db, long id, string source, string tab)
            {
                await start.Task;
                try
                {
                    await db.CommitMonitorConversationHandoffAsync(
                        id, source, tab, tab, targetUrl, true, true, tab + "-old", "AssistantMessageCount", "continue", "trigger", "Rotated", "Sent");
                    return (true, null);
                }
                catch (Exception ex) { return (false, ex); }
            }

            var firstTask = Task.Run(() => RunAsync(firstDb, firstId, "https://chatgpt.com/c/source-a", "new-a"));
            var secondTask = Task.Run(() => RunAsync(secondDb, secondId, "https://chatgpt.com/c/source-b", "new-b"));
            start.SetResult();
            var outcomes = await Task.WhenAll(firstTask, secondTask);

            Assert.Single(outcomes, outcome => outcome.Succeeded);
            Assert.Single(outcomes, outcome => !outcome.Succeeded);
            Assert.Contains(outcomes, outcome => outcome.Error is InvalidOperationException);
            var monitors = await firstDb.GetSavedMonitorsAsync();
            Assert.Single(monitors, item => string.Equals(item.Url, targetUrl, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ConcurrentSourceRebindAndHandoffCannotOverwriteEachOther()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var handoffDb = new LocalDatabase(path);
            var repairDb = new LocalDatabase(path);
            await handoffDb.InitializeAsync();
            const string sourceUrl = "https://chatgpt.com/c/concurrent-source";
            var monitorId = await handoffDb.SaveMonitorAsync(new SavedMonitor { TabId = "old", Title = "Old", Url = sourceUrl });
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var handoffTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await handoffDb.CommitMonitorConversationHandoffAsync(
                        monitorId, sourceUrl, "handoff", "Handoff", "https://chatgpt.com/c/handoff-wins",
                        true, true, "old", "AssistantMessageCount", "continue", "trigger", "Rotated", "Sent");
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            });

            var repairTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await repairDb.RebindMonitorConversationIfAvailableAsync(
                        monitorId, sourceUrl, "repair", "Repair", "https://chatgpt.com/c/repair-wins",
                        requireDuplicateSourceOwnership: false,
                        diagnosticPrompt: "repair",
                        diagnosticResponse: "repair",
                        diagnosticStatus: "RepairWon");
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            });

            start.SetResult();
            var outcomes = await Task.WhenAll(handoffTask, repairTask);
            Assert.Single(outcomes, succeeded => succeeded);

            var saved = Assert.Single(await handoffDb.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.True(
                string.Equals(saved.Url, "https://chatgpt.com/c/handoff-wins", StringComparison.Ordinal)
                || string.Equals(saved.Url, "https://chatgpt.com/c/repair-wins", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void MonitorRuntimeUsesStablePostSendTargetAndTransactionalHandoffForAllConversationChangingPaths()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.Contains("ResolveStableCreatedConversationAsync", source, StringComparison.Ordinal);
        Assert.Contains("CommitMonitorConversationHandoffAsync", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"ConversationContextLimit\"", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"AssistantMessageCount\"", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"DeliveryTimeout\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.Url = newTab.Url", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await _database.SaveMonitorAsync(monitor", source, StringComparison.Ordinal);
    }

    private static async Task<int> CountRotationRowsAsync(string databasePath, long monitorId)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ConversationRotations WHERE MonitorId=$id;";
        command.Parameters.AddWithValue("$id", monitorId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch { }
    }
}
''', encoding="utf-8")

replace_once(
    "docs/MESSAGE_COUNT_ROTATION.md",
    '''6. Only after verified delivery, increment the rotation count, persist the new Tab ID/title/URL under the same Monitor ID, and record the rotation.\n7. Close the old chat only after the new-chat handoff has succeeded.\n''',
    '''6. After verified delivery, re-enumerate the same Chrome target until it exposes the stable `/c/{conversation-id}` URL created by ChatGPT.\n7. Commit the identity move through one immediate SQLite writer transaction: the old saved conversation must still match the monitor snapshot, the new stable conversation must be unowned, RotationCount is incremented, and the rotation + success receipts are written atomically under the same Monitor ID.\n8. Close the old chat only after that transaction commits successfully.\n''')

replace_once(
    "docs/MESSAGE_COUNT_ROTATION.md",
    '''If verified delivery fails, the new unused tab is closed, the old conversation remains authoritative, and the same rotation remains eligible for a later retry.\n''',
    '''If verified delivery fails, the new unused tab is closed, the old conversation remains authoritative, and the same rotation remains eligible for a later retry. If the post-send target never exposes a stable conversation URL, another monitor owns the new URL, or the source monitor binding changed concurrently, the new tab is left unclaimed/closed and the old tab is not closed by the handoff path.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''- Crash Recovery Stable-Conversation Binding — PR #68: recovery now applies the same saved-conversation identity invariant to persisted target reuse, normalized URL fallback and newly created tabs. Recovery never mutates the persisted conversation URL, writes only guarded runtime target metadata before any send/start, and records `CrashRecoverySavedConversationChanged` while keeping the incident pending if its saved URL snapshot becomes stale. Redirected or reused targets for another conversation are rejected before delivery.\n''',
    '''- Crash Recovery Stable-Conversation Binding is merged on `main` (`cb09ac8fe8e1a189dbb9b721f5519bb314290f7d`): recovery now applies the same saved-conversation identity invariant to persisted target reuse, normalized URL fallback and newly created tabs. Recovery never mutates the persisted conversation URL, writes only guarded runtime target metadata before any send/start, and records `CrashRecoverySavedConversationChanged` while keeping the incident pending if its saved URL snapshot becomes stale. Redirected or reused targets for another conversation are rejected before delivery.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After crash-recovery stable-conversation binding, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #69 is the current tracked post-1.8 task: make every intentional conversation-changing runtime handoff transactional, resolve the final stable `/c/{conversation-id}` URL after verified new-chat delivery, atomically claim that unowned target from the expected old conversation, and commit rotation/recovery receipts without broad `SaveMonitorAsync` identity mutation. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #69 patch applied.")
