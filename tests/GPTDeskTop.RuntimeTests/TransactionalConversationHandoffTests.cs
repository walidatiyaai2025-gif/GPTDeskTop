using GPTDeskTop.Data;
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
