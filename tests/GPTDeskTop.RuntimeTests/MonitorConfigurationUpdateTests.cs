using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorConfigurationUpdateTests
{
    [Fact]
    public async Task StaleSettingsSnapshotAfterIntentionalHandoffCannotRollbackBindingOrRotationCount()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "test.db");
            var database = new LocalDatabase(path);
            await database.InitializeAsync();
            var staleSettingsSnapshot = new SavedMonitor
            {
                TabId = "old-target",
                Title = "Old title",
                Url = "https://chatgpt.com/c/settings-source",
                AutoReply = "old-reply",
                ReplyDelaySeconds = 3,
                TimerSeconds = 1,
                Enabled = true,
                ConversationRotationEnabled = true,
                NewChatStartMessage = "old-start",
                NewChatDelaySeconds = 2,
                RotationCooldownSeconds = 3,
                MaxConversationRotations = 10,
                RotationCount = 4,
                ModelRoutingEnabled = false,
                PreferredModel = "Auto",
                FallbackModel = "Auto"
            };
            var monitorId = await database.SaveMonitorAsync(staleSettingsSnapshot);

            await database.CommitMonitorConversationHandoffAsync(
                monitorId,
                "https://chatgpt.com/c/settings-source",
                "new-target",
                "New title",
                "https://chatgpt.com/c/settings-handoff",
                incrementRotationCount: true,
                recordRotation: true,
                oldTabId: "old-target",
                rotationTrigger: "AssistantMessageCount",
                startMessage: "continue",
                triggerResponse: "trigger",
                successStatus: "Rotated",
                outboundStatus: "Sent");

            staleSettingsSnapshot.AutoReply = "new-reply";
            staleSettingsSnapshot.ReplyDelaySeconds = 21;
            staleSettingsSnapshot.TimerSeconds = 8;
            staleSettingsSnapshot.Enabled = false;
            staleSettingsSnapshot.NewChatStartMessage = "new-start";
            staleSettingsSnapshot.NewChatDelaySeconds = 33;
            staleSettingsSnapshot.RotationCooldownSeconds = 44;
            staleSettingsSnapshot.MaxConversationRotations = 55;
            staleSettingsSnapshot.ModelRoutingEnabled = true;
            staleSettingsSnapshot.PreferredModel = "GPT-5";
            staleSettingsSnapshot.FallbackModel = "Auto";
            Assert.True(await database.UpdateMonitorConfigurationAsync(staleSettingsSnapshot));

            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("new-target", saved.TabId);
            Assert.Equal("New title", saved.Title);
            Assert.Equal("https://chatgpt.com/c/settings-handoff", saved.Url);
            Assert.Equal(5, saved.RotationCount);
            Assert.Equal("new-reply", saved.AutoReply);
            Assert.Equal(21, saved.ReplyDelaySeconds);
            Assert.Equal(8, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.Equal("new-start", saved.NewChatStartMessage);
            Assert.Equal(33, saved.NewChatDelaySeconds);
            Assert.Equal(44, saved.RotationCooldownSeconds);
            Assert.Equal(55, saved.MaxConversationRotations);
            Assert.True(saved.ModelRoutingEnabled);
            Assert.Equal("GPT-5", saved.PreferredModel);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StaleSettingsSnapshotAfterRepairCannotRollbackReboundConversation()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var staleSettingsSnapshot = new SavedMonitor
            {
                TabId = "legacy-target",
                Title = "Legacy",
                Url = "https://chatgpt.com/",
                AutoReply = "before",
                RotationCount = 9
            };
            var monitorId = await database.SaveMonitorAsync(staleSettingsSnapshot);

            await database.RebindMonitorConversationIfAvailableAsync(
                monitorId,
                "https://chatgpt.com/",
                "repair-target",
                "Repaired",
                "https://chatgpt.com/c/repaired-settings-monitor",
                requireDuplicateSourceOwnership: false,
                diagnosticPrompt: "repair",
                diagnosticResponse: "rebound",
                diagnosticStatus: "MonitorConversationIdentityRebound");

            staleSettingsSnapshot.AutoReply = "after";
            staleSettingsSnapshot.ReplyDelaySeconds = 19;
            Assert.True(await database.UpdateMonitorConfigurationAsync(staleSettingsSnapshot));

            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("repair-target", saved.TabId);
            Assert.Equal("Repaired", saved.Title);
            Assert.Equal("https://chatgpt.com/c/repaired-settings-monitor", saved.Url);
            Assert.Equal(9, saved.RotationCount);
            Assert.Equal("after", saved.AutoReply);
            Assert.Equal(19, saved.ReplyDelaySeconds);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ConfigurationUpdateReturnsFalseWhenMonitorWasDeleted()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "target",
                Title = "Deleted",
                Url = "https://chatgpt.com/c/deleted-settings-monitor"
            };
            var monitorId = await database.SaveMonitorAsync(monitor);
            await database.DeleteMonitorAsync(monitorId);

            monitor.AutoReply = "should-not-save";
            Assert.False(await database.UpdateMonitorConfigurationAsync(monitor));
            Assert.Empty(await database.GetSavedMonitorsAsync());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ExistingMonitorUiEditUsesConfigOnlyDatabaseUpdate()
    {
        var mainForm = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var database = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");

        var editStart = mainForm.IndexOf("private async Task EditSelectedMonitorSettingsAsync", StringComparison.Ordinal);
        var editEnd = mainForm.IndexOf("private async Task DeleteSelectedMonitorAsync", editStart, StringComparison.Ordinal);
        var editBlock = mainForm[editStart..editEnd];

        Assert.Contains("UpdateMonitorConfigurationAsync(_selectedMonitor)", editBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveMonitorAsync(_selectedMonitor)", editBlock, StringComparison.Ordinal);
        Assert.Contains("await RefreshMonitorsAsync()", editBlock, StringComparison.Ordinal);

        var methodStart = database.IndexOf("public async Task<bool> UpdateMonitorConfigurationAsync", StringComparison.Ordinal);
        var methodEnd = database.IndexOf("public async Task<bool> UpdateMonitorRuntimeTargetIfConversationMatchesAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = database[methodStart..methodEnd];
        Assert.DoesNotContain("TabId=", methodBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Title=", methodBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Url=", methodBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RotationCount=", methodBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutoReply=$autoReply", methodBlock, StringComparison.Ordinal);
        Assert.Contains("WHERE Id=$id", methodBlock, StringComparison.Ordinal);
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
