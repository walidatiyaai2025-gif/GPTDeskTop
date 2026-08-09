using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorIdentityRepairServiceTests
{
    [Fact]
    public async Task RebindPreservesMonitorIdentityConfigurationHistoryAndPendingRecovery()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");

            var monitor = new SavedMonitor
            {
                TabId = "legacy-tab",
                Title = "Legacy monitor",
                Url = "https://chatgpt.com/",
                AutoReply = "continue safely",
                ReplyDelaySeconds = 9,
                TimerSeconds = 4,
                Enabled = false,
                ConversationRotationEnabled = true,
                NewChatStartMessage = "resume",
                NewChatDelaySeconds = 44,
                RotationCooldownSeconds = 77,
                MaxConversationRotations = 12,
                RotationCount = 7,
                ModelRoutingEnabled = true,
                PreferredModel = "GPT-5",
                FallbackModel = "Auto"
            };
            var monitorId = await database.SaveMonitorAsync(monitor);
            await database.AddLogAsync("Inbound", "before", "history", "Detected", monitorId, "legacy-tab", "Legacy monitor");

            var service = new MonitorIdentityRepairService(database);
            var result = await service.RebindAsync(
                monitorId,
                new ChromeTab
                {
                    Id = "replacement-tab",
                    Title = "Replacement conversation",
                    Url = "https://chatgpt.com/c/repaired-monitor-123"
                });

            Assert.Equal(monitorId, result.MonitorId);
            Assert.Equal("https://chatgpt.com/", result.PreviousUrl);
            Assert.Equal("https://chatgpt.com/c/repaired-monitor-123", result.NewUrl);
            Assert.True(result.CrashRecoveryPending);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));

            var saved = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal(monitorId, saved.Id);
            Assert.Equal("replacement-tab", saved.TabId);
            Assert.Equal("Replacement conversation", saved.Title);
            Assert.Equal("https://chatgpt.com/c/repaired-monitor-123", saved.Url);
            Assert.Equal("continue safely", saved.AutoReply);
            Assert.Equal(9, saved.ReplyDelaySeconds);
            Assert.Equal(4, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.True(saved.ConversationRotationEnabled);
            Assert.Equal("resume", saved.NewChatStartMessage);
            Assert.Equal(44, saved.NewChatDelaySeconds);
            Assert.Equal(77, saved.RotationCooldownSeconds);
            Assert.Equal(12, saved.MaxConversationRotations);
            Assert.Equal(7, saved.RotationCount);
            Assert.True(saved.ModelRoutingEnabled);
            Assert.Equal("GPT-5", saved.PreferredModel);
            Assert.Equal("Auto", saved.FallbackModel);

            var history = await database.GetRecentLogsForMonitorAsync(monitorId, 10);
            Assert.Equal(2, history.Count);
            Assert.Contains(history, log => log.Prompt == "before" && log.Response == "history");
            Assert.Contains(history, log => log.Status == "MonitorConversationIdentityRebound" && log.TabId == "replacement-tab");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsConversationAlreadyOwnedByAnotherMonitor()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var source = new SavedMonitor { TabId = "bad", Title = "Bad", Url = "https://chatgpt.com/" };
            var sourceId = await database.SaveMonitorAsync(source);
            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "owner-tab",
                Title = "Owner",
                Url = "https://chatgpt.com/c/already-owned"
            });

            var service = new MonitorIdentityRepairService(database);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                sourceId,
                new ChromeTab { Id = "new-tab", Title = "Duplicate", Url = "https://chatgpt.com/c/already-owned" }));

            Assert.Contains("already owns", exception.Message, StringComparison.OrdinalIgnoreCase);
            var unchanged = Assert.Single((await database.GetSavedMonitorsAsync()).Where(saved => saved.Id == sourceId));
            Assert.Equal("bad", unchanged.TabId);
            Assert.Equal("https://chatgpt.com/", unchanged.Url);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsValidSourceAndInvalidTarget()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var valid = new SavedMonitor { TabId = "valid-tab", Title = "Valid", Url = "https://chatgpt.com/c/already-valid" };
            var validId = await database.SaveMonitorAsync(valid);
            var invalid = new SavedMonitor { TabId = "invalid-tab", Title = "Invalid", Url = "https://chatgpt.com/" };
            var invalidId = await database.SaveMonitorAsync(invalid);
            var service = new MonitorIdentityRepairService(database);

            var validSourceError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                validId,
                new ChromeTab { Id = "target", Title = "Target", Url = "https://chatgpt.com/c/new-target" }));
            Assert.Contains("does not need repair", validSourceError.Message, StringComparison.OrdinalIgnoreCase);

            var invalidTargetError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                invalidId,
                new ChromeTab { Id = "home", Title = "Home", Url = "https://chatgpt.com/" }));
            Assert.Contains("not a stable ChatGPT conversation", invalidTargetError.Message, StringComparison.OrdinalIgnoreCase);
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