using GPTDeskTop.Data;
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
            Assert.Single(monitors, monitor => string.Equals(monitor.Url, targetUrl, StringComparison.OrdinalIgnoreCase));
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

            Assert.Single(outcomes, outcome => outcome.Succeeded);
            Assert.Single(outcomes, outcome => !outcome.Succeeded);
            Assert.Contains(outcomes, outcome => outcome.Error is InvalidOperationException);

            var monitors = await firstDb.GetSavedMonitorsAsync();
            Assert.Single(monitors, monitor => string.Equals(monitor.Url, targetUrl, StringComparison.OrdinalIgnoreCase));
            Assert.Single(monitors, monitor => string.Equals(monitor.Url, duplicateUrl, StringComparison.OrdinalIgnoreCase));
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
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
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
        Assert.Contains("FindLogicalConversationOwnerIdAsync", database, StringComparison.Ordinal);
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
