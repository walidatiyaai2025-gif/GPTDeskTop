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
    '''public sealed record ConfigurationImportDatabaseResult(int SettingsApplied, int MonitorsUpdated, int MonitorsInserted);\npublic sealed record MonitorRegistrationResult(long MonitorId, bool Created);\n''',
    '''public sealed record ConfigurationImportDatabaseResult(int SettingsApplied, int MonitorsUpdated, int MonitorsInserted);\npublic sealed record MonitorRegistrationResult(long MonitorId, bool Created);\npublic sealed record MonitorConversationRebindDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl);\n''')

replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''    private static void ClampMonitorSettings(SavedMonitor monitor)\n''',
    r'''    public async Task<MonitorConversationRebindDatabaseResult> RebindMonitorConversationIfAvailableAsync(
        long monitorId,
        string expectedCurrentUrl,
        string targetTabId,
        string targetTitle,
        string targetUrl,
        bool requireDuplicateSourceOwnership,
        string diagnosticPrompt,
        string diagnosticResponse,
        string diagnosticStatus,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The selected ChatGPT conversation does not have a usable Chrome target ID.");
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new InvalidOperationException("A target conversation URL is required for monitor repair.");

        expectedCurrentUrl ??= string.Empty;
        targetTitle ??= string.Empty;
        diagnosticPrompt ??= string.Empty;
        diagnosticResponse ??= string.Empty;
        diagnosticStatus ??= string.Empty;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            string currentUrl;
            string currentTitle;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url, Title FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                await using var reader = await load.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");
                currentUrl = reader.GetString(0);
                currentTitle = reader.GetString(1);
            }

            if (!string.Equals(currentUrl, expectedCurrentUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("Saved monitor conversation identity changed before repair could be applied. Refresh and try again.");

            if (string.Equals(currentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve conversation ownership.");

            if (requireDuplicateSourceOwnership)
            {
                await using var sourceOwners = connection.CreateCommand();
                sourceOwners.Transaction = transaction;
                sourceOwners.CommandText = "SELECT COUNT(*) FROM SavedMonitors WHERE Url=$url COLLATE NOCASE;";
                sourceOwners.Parameters.AddWithValue("$url", currentUrl);
                var sourceOwnerCount = Convert.ToInt32(await sourceOwners.ExecuteScalarAsync(cancellationToken));
                if (sourceOwnerCount < 2)
                    throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");
            }

            await using (var targetOwner = connection.CreateCommand())
            {
                targetOwner.Transaction = transaction;
                targetOwner.CommandText = "SELECT Id FROM SavedMonitors WHERE Id<>$id AND Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                targetOwner.Parameters.AddWithValue("$id", monitorId);
                targetOwner.Parameters.AddWithValue("$url", targetUrl);
                var existing = await targetOwner.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                    throw new InvalidOperationException($"Monitor #{Convert.ToInt64(existing)} already owns the selected ChatGPT conversation.");
            }

            var now = DateTime.UtcNow.ToString("O");
            var appliedTitle = string.IsNullOrWhiteSpace(targetTitle) ? currentTitle : targetTitle;
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, Url=$url, UpdatedAt=$updatedAt WHERE Id=$id;";
                update.Parameters.AddWithValue("$id", monitorId);
                update.Parameters.AddWithValue("$tabId", targetTabId);
                update.Parameters.AddWithValue("$title", appliedTitle);
                update.Parameters.AddWithValue("$url", targetUrl);
                update.Parameters.AddWithValue("$updatedAt", now);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Saved monitor #{monitorId} could not be updated.");
            }

            await using (var insertLog = connection.CreateCommand())
            {
                insertLog.Transaction = transaction;
                insertLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,$dir,$p,$r,$s);";
                insertLog.Parameters.AddWithValue("$ts", now);
                insertLog.Parameters.AddWithValue("$m", monitorId);
                insertLog.Parameters.AddWithValue("$id", targetTabId);
                insertLog.Parameters.AddWithValue("$title", appliedTitle);
                insertLog.Parameters.AddWithValue("$dir", "System");
                insertLog.Parameters.AddWithValue("$p", diagnosticPrompt);
                insertLog.Parameters.AddWithValue("$r", diagnosticResponse);
                insertLog.Parameters.AddWithValue("$s", diagnosticStatus);
                await insertLog.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new MonitorConversationRebindDatabaseResult(monitorId, currentUrl, targetUrl);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static void ClampMonitorSettings(SavedMonitor monitor)
''')

replace_once(
    "src/GPTDeskTop/Services/MonitorIdentityRepairService.cs",
    '''        var previousUrl = monitor.Url ?? string.Empty;\n        monitor.TabId = targetTab.Id;\n        monitor.Title = string.IsNullOrWhiteSpace(targetTab.Title) ? monitor.Title : targetTab.Title;\n        monitor.Url = targetTab.Url;\n\n        await _database.SaveMonitorAsync(monitor, cancellationToken);\n        await _database.AddLogAsync(\n            "System",\n            "Monitor identity repair",\n            $"Rebound monitor #{monitor.Id} from an invalid saved identity to a stable ChatGPT conversation.",\n            "MonitorConversationIdentityRebound",\n            monitor.Id,\n            monitor.TabId,\n            monitor.Title,\n            cancellationToken);\n\n        var pending = string.Equals(\n            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),\n            "1",\n            StringComparison.Ordinal);\n\n        return new MonitorIdentityRebindResult(monitor.Id, previousUrl, monitor.Url, pending);\n''',
    '''        var rebind = await _database.RebindMonitorConversationIfAvailableAsync(\n            monitor.Id,\n            monitor.Url ?? string.Empty,\n            targetTab.Id,\n            targetTab.Title,\n            targetTab.Url,\n            requireDuplicateSourceOwnership: false,\n            diagnosticPrompt: "Monitor identity repair",\n            diagnosticResponse: $"Rebound monitor #{monitor.Id} from an invalid saved identity to a stable ChatGPT conversation.",\n            diagnosticStatus: "MonitorConversationIdentityRebound",\n            cancellationToken);\n\n        var pending = string.Equals(\n            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),\n            "1",\n            StringComparison.Ordinal);\n\n        return new MonitorIdentityRebindResult(rebind.MonitorId, rebind.PreviousUrl, rebind.NewUrl, pending);\n''')

replace_once(
    "src/GPTDeskTop/Services/DuplicateOwnershipRepairService.cs",
    '''        var previousUrl = monitor.Url ?? string.Empty;\n        monitor.TabId = targetTab.Id;\n        monitor.Title = string.IsNullOrWhiteSpace(targetTab.Title) ? monitor.Title : targetTab.Title;\n        monitor.Url = targetTab.Url;\n\n        await _database.SaveMonitorAsync(monitor, cancellationToken);\n        await _database.AddLogAsync(\n            "System",\n            "Monitor ownership repair",\n            $"Rebound duplicate owner monitor #{monitor.Id} to a different unowned stable ChatGPT conversation.",\n            "MonitorDuplicateConversationOwnershipRebound",\n            monitor.Id,\n            monitor.TabId,\n            monitor.Title,\n            cancellationToken);\n\n        var pending = string.Equals(\n            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),\n            "1",\n            StringComparison.Ordinal);\n\n        return new MonitorIdentityRebindResult(monitor.Id, previousUrl, monitor.Url, pending);\n''',
    '''        var rebind = await _database.RebindMonitorConversationIfAvailableAsync(\n            monitor.Id,\n            monitor.Url ?? string.Empty,\n            targetTab.Id,\n            targetTab.Title,\n            targetTab.Url,\n            requireDuplicateSourceOwnership: true,\n            diagnosticPrompt: "Monitor ownership repair",\n            diagnosticResponse: $"Rebound duplicate owner monitor #{monitor.Id} to a different unowned stable ChatGPT conversation.",\n            diagnosticStatus: "MonitorDuplicateConversationOwnershipRebound",\n            cancellationToken);\n\n        var pending = string.Equals(\n            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),\n            "1",\n            StringComparison.Ordinal);\n\n        return new MonitorIdentityRebindResult(rebind.MonitorId, rebind.PreviousUrl, rebind.NewUrl, pending);\n''')

Path("tests/GPTDeskTop.RuntimeTests/TransactionalConversationRebindTests.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class TransactionalConversationRebindTests
{
    [Fact]
    public async Task InvalidRepairCompetingWithRegistrationCannotCreateDuplicateTargetOwnership()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var repairDb = new LocalDatabase(path);
            var registrationDb = new LocalDatabase(path);
            await repairDb.InitializeAsync();

            var invalidId = await repairDb.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "legacy",
                Title = "Legacy invalid",
                Url = "https://chatgpt.com/"
            });

            const string targetUrl = "https://chatgpt.com/c/transactional-target";
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var repairTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var service = new MonitorIdentityRepairService(repairDb);
                    await service.RebindAsync(invalidId, new ChromeTab { Id = "repair-target", Title = "Repair target", Url = targetUrl });
                    return (Succeeded: true, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Error: ex);
                }
            });
            var registrationTask = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var result = await registrationDb.RegisterMonitorIfConversationAvailableAsync(new SavedMonitor
                    {
                        TabId = "registration-target",
                        Title = "Registration target",
                        Url = targetUrl
                    });
                    return (Succeeded: true, Result: result, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Result: (MonitorRegistrationResult?)null, Error: ex);
                }
            });

            start.SetResult();
            var repairOutcome = await repairTask;
            var registrationOutcome = await registrationTask;

            Assert.True(repairOutcome.Succeeded || registrationOutcome.Succeeded);
            var monitors = await repairDb.GetSavedMonitorsAsync();
            Assert.Single(monitors.Where(monitor => string.Equals(monitor.Url, targetUrl, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(monitors.GroupBy(monitor => monitor.Url, StringComparer.OrdinalIgnoreCase), group => group.Count() > 1 && string.Equals(group.Key, targetUrl, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CompetingDuplicateRepairsCannotMoveTwoOwnersToSameTarget()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var firstDb = new LocalDatabase(path);
            var secondDb = new LocalDatabase(path);
            await firstDb.InitializeAsync();

            const string duplicateUrl = "https://chatgpt.com/c/legacy-duplicate-race";
            var first = new SavedMonitor { TabId = "a", Title = "A", Url = duplicateUrl };
            var firstId = await firstDb.SaveMonitorAsync(first);
            var second = new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/temporary-b" };
            var secondId = await firstDb.SaveMonitorAsync(second);
            second.Url = duplicateUrl;
            await firstDb.SaveMonitorAsync(second);

            const string targetUrl = "https://chatgpt.com/c/shared-repair-target";
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<(bool Succeeded, Exception? Error)> RunRepairAsync(LocalDatabase db, long monitorId, string tabId)
            {
                await start.Task;
                try
                {
                    var service = new DuplicateOwnershipRepairService(db);
                    await service.RebindAsync(monitorId, new ChromeTab { Id = tabId, Title = tabId, Url = targetUrl });
                    return (true, null);
                }
                catch (Exception ex)
                {
                    return (false, ex);
                }
            }

            var firstTask = Task.Run(() => RunRepairAsync(firstDb, firstId, "target-a"));
            var secondTask = Task.Run(() => RunRepairAsync(secondDb, secondId, "target-b"));
            start.SetResult();
            var outcomes = await Task.WhenAll(firstTask, secondTask);

            Assert.Single(outcomes.Where(outcome => outcome.Succeeded));
            Assert.Single(outcomes.Where(outcome => !outcome.Succeeded));
            Assert.Contains(outcomes, outcome => outcome.Error is InvalidOperationException);

            var monitors = await firstDb.GetSavedMonitorsAsync();
            Assert.Single(monitors.Where(monitor => string.Equals(monitor.Url, targetUrl, StringComparison.OrdinalIgnoreCase)));
            Assert.Single(monitors.Where(monitor => string.Equals(monitor.Url, duplicateUrl, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(0, MonitorConversationOwnership.CountDuplicateMonitors(monitors));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task TransactionRejectsStaleSourceSnapshotWithoutChangingBindingOrWritingReceipt()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "old",
                Title = "Stale source",
                Url = "https://chatgpt.com/"
            };
            var monitorId = await database.SaveMonitorAsync(monitor);
            monitor.Url = "https://chatgpt.com/share/current-source";
            await database.SaveMonitorAsync(monitor);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.RebindMonitorConversationIfAvailableAsync(
                    monitorId,
                    "https://chatgpt.com/",
                    "target",
                    "Target",
                    "https://chatgpt.com/c/stale-target",
                    requireDuplicateSourceOwnership: false,
                    diagnosticPrompt: "repair",
                    diagnosticResponse: "should not commit",
                    diagnosticStatus: "ShouldNotExist"));

            Assert.Contains("changed before repair", error.Message, StringComparison.OrdinalIgnoreCase);
            var saved = Assert.Single((await database.GetSavedMonitorsAsync()).Where(item => item.Id == monitorId));
            Assert.Equal("https://chatgpt.com/share/current-source", saved.Url);
            var history = await database.GetRecentLogsForMonitorAsync(monitorId, 20);
            Assert.DoesNotContain(history, log => log.Status == "ShouldNotExist");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void RepairServicesUseSharedTransactionalPrimitiveWithoutSeparateSaveOrLog()
    {
        var identity = ReadSource("src", "GPTDeskTop", "Services", "MonitorIdentityRepairService.cs");
        var duplicate = ReadSource("src", "GPTDeskTop", "Services", "DuplicateOwnershipRepairService.cs");
        var database = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");

        Assert.Contains("RebindMonitorConversationIfAvailableAsync", identity, StringComparison.Ordinal);
        Assert.Contains("RebindMonitorConversationIfAvailableAsync", duplicate, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveMonitorAsync(monitor", identity, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLogAsync", identity, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveMonitorAsync(monitor", duplicate, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLogAsync", duplicate, StringComparison.Ordinal);
        Assert.Contains("BeginTransaction(deferred: false)", database, StringComparison.Ordinal);
        Assert.Contains("targetOwner.Transaction = transaction", database, StringComparison.Ordinal);
        Assert.Contains("insertLog.Transaction = transaction", database, StringComparison.Ordinal);
        Assert.Contains("COLLATE NOCASE", database, StringComparison.Ordinal);
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
    "docs/DEVELOPMENT_STATUS.md",
    '''- Safe Duplicate Ownership Remediation is implemented in PR #62: a guarded duplicate-owner-only rebind path moves exactly one duplicate owner to a different currently open unowned stable ChatGPT conversation while preserving the same Monitor ID, history association, automation settings, rotation configuration/count and crash-recovery pending state. Runtime Health Repair now handles invalid identities or duplicate owners, the Repair dialog exposes only safe unowned stable targets, and `MonitorDuplicateConversationOwnershipRebound` provides explicit remediation telemetry without clearing recovery state.\n''',
    '''- Safe Duplicate Ownership Remediation is merged on `main` (`919147398f3b0e15b71597f7cdfb88181daea917`): a guarded duplicate-owner-only rebind path moves exactly one duplicate owner to a different currently open unowned stable ChatGPT conversation while preserving the same Monitor ID, history association, automation settings, rotation configuration/count and crash-recovery pending state. Runtime Health Repair now handles invalid identities or duplicate owners, the Repair dialog exposes only safe unowned stable targets, and `MonitorDuplicateConversationOwnershipRebound` provides explicit remediation telemetry without clearing recovery state.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After the safe duplicate-ownership remediation, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #63 is the current tracked post-1.8 task: make invalid-identity and duplicate-owner conversation rebinding ownership-safe under concurrency by using one immediate SQLite writer transaction that revalidates the source snapshot, verifies the target remains unowned, updates the existing monitor row and records the repair diagnostic atomically. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #63 patch applied.")
