using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipRepairServiceTests
{
    [Fact]
    public async Task RebindDuplicateOwnerPreservesIdentityConfigurationHistoryAndPendingRecovery()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");

            const string duplicateUrl = "https://chatgpt.com/c/legacy-duplicate";
            var firstId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "owner-a",
                Title = "Owner A",
                Url = duplicateUrl
            });

            var duplicate = new SavedMonitor
            {
                TabId = "owner-b-old",
                Title = "Owner B",
                Url = "https://chatgpt.com/c/temporary-unique",
                AutoReply = "continue duplicate safely",
                ReplyDelaySeconds = 11,
                TimerSeconds = 5,
                Enabled = false,
                ConversationRotationEnabled = true,
                NewChatStartMessage = "resume duplicate",
                NewChatDelaySeconds = 45,
                RotationCooldownSeconds = 78,
                MaxConversationRotations = 13,
                RotationCount = 8,
                ModelRoutingEnabled = true,
                PreferredModel = "GPT-5",
                FallbackModel = "Auto"
            };
            var duplicateId = await database.SaveMonitorAsync(duplicate);
            duplicate.Url = duplicateUrl;
            await database.SaveMonitorAsync(duplicate);
            await database.AddLogAsync("Inbound", "before-duplicate", "history", "Detected", duplicateId, "owner-b-old", "Owner B");

            var before = await database.GetSavedMonitorsAsync();
            Assert.Equal(2, MonitorConversationOwnership.CountDuplicateMonitors(before));
            Assert.True(MonitorConversationOwnership.IsDuplicateOwner(firstId, before));
            Assert.True(MonitorConversationOwnership.IsDuplicateOwner(duplicateId, before));

            var service = new DuplicateOwnershipRepairService(database);
            var result = await service.RebindAsync(
                duplicateId,
                new ChromeTab
                {
                    Id = "owner-b-new",
                    Title = "Owner B replacement",
                    Url = "https://chatgpt.com/c/unowned-replacement"
                });

            Assert.Equal(duplicateId, result.MonitorId);
            Assert.Equal(duplicateUrl, result.PreviousUrl);
            Assert.Equal("https://chatgpt.com/c/unowned-replacement", result.NewUrl);
            Assert.True(result.CrashRecoveryPending);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));

            var savedMonitors = await database.GetSavedMonitorsAsync();
            Assert.Equal(0, MonitorConversationOwnership.CountDuplicateMonitors(savedMonitors));
            var saved = Assert.Single(savedMonitors.Where(monitor => monitor.Id == duplicateId));
            Assert.Equal("owner-b-new", saved.TabId);
            Assert.Equal("Owner B replacement", saved.Title);
            Assert.Equal("https://chatgpt.com/c/unowned-replacement", saved.Url);
            Assert.Equal("continue duplicate safely", saved.AutoReply);
            Assert.Equal(11, saved.ReplyDelaySeconds);
            Assert.Equal(5, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.True(saved.ConversationRotationEnabled);
            Assert.Equal("resume duplicate", saved.NewChatStartMessage);
            Assert.Equal(45, saved.NewChatDelaySeconds);
            Assert.Equal(78, saved.RotationCooldownSeconds);
            Assert.Equal(13, saved.MaxConversationRotations);
            Assert.Equal(8, saved.RotationCount);
            Assert.True(saved.ModelRoutingEnabled);
            Assert.Equal("GPT-5", saved.PreferredModel);
            Assert.Equal("Auto", saved.FallbackModel);

            var history = await database.GetRecentLogsForMonitorAsync(duplicateId, 10);
            Assert.Equal(2, history.Count);
            Assert.Contains(history, log => log.Prompt == "before-duplicate" && log.Response == "history");
            Assert.Contains(history, log => log.Status == "MonitorDuplicateConversationOwnershipRebound" && log.TabId == "owner-b-new");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsUniqueOwnerAndConversationOwnedByAnotherMonitor()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var uniqueId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "unique",
                Title = "Unique",
                Url = "https://chatgpt.com/c/unique-source"
            });
            var service = new DuplicateOwnershipRepairService(database);
            var uniqueError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                uniqueId,
                new ChromeTab { Id = "free", Title = "Free", Url = "https://chatgpt.com/c/free-target" }));
            Assert.Contains("not currently part of duplicate", uniqueError.Message, StringComparison.OrdinalIgnoreCase);

            const string duplicateUrl = "https://chatgpt.com/c/duplicate-source";
            var first = new SavedMonitor { TabId = "a", Title = "A", Url = duplicateUrl };
            var firstId = await database.SaveMonitorAsync(first);
            var second = new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/temporary-b" };
            var secondId = await database.SaveMonitorAsync(second);
            second.Url = duplicateUrl;
            await database.SaveMonitorAsync(second);

            var ownedTargetId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "target-owner",
                Title = "Target owner",
                Url = "https://chatgpt.com/c/already-owned-target"
            });

            var ownedError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                secondId,
                new ChromeTab { Id = "target", Title = "Target", Url = "https://chatgpt.com/c/already-owned-target" }));
            Assert.Contains($"Monitor #{ownedTargetId} already owns", ownedError.Message, StringComparison.OrdinalIgnoreCase);

            var sameError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                firstId,
                new ChromeTab { Id = "same", Title = "Same", Url = duplicateUrl }));
            Assert.Contains("different unowned", sameError.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsInvalidTargetWithoutMutatingDuplicateOwner()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            const string duplicateUrl = "https://chatgpt.com/c/duplicate-invalid-target";
            var first = new SavedMonitor { TabId = "a", Title = "A", Url = duplicateUrl };
            await database.SaveMonitorAsync(first);
            var second = new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/temporary" };
            var secondId = await database.SaveMonitorAsync(second);
            second.Url = duplicateUrl;
            await database.SaveMonitorAsync(second);

            var service = new DuplicateOwnershipRepairService(database);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                secondId,
                new ChromeTab { Id = "home", Title = "Home", Url = "https://chatgpt.com/" }));

            Assert.Contains("not a stable ChatGPT conversation", error.Message, StringComparison.OrdinalIgnoreCase);
            var unchanged = Assert.Single((await database.GetSavedMonitorsAsync()).Where(monitor => monitor.Id == secondId));
            Assert.Equal("b", unchanged.TabId);
            Assert.Equal(duplicateUrl, unchanged.Url);
        }
        finally
        {
            DeleteTempRoot(root);
        }
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
