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
    '''    public async Task<bool> UpdateMonitorRuntimeTargetIfConversationMatchesAsync(\n''',
    r'''    public async Task<bool> UpdateMonitorConfigurationAsync(
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Id <= 0)
            throw new InvalidOperationException("Monitor configuration can only be updated after the monitor is saved.");

        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SavedMonitors SET
                AutoReply=$autoReply,
                ReplyDelaySeconds=$replyDelay,
                TimerSeconds=$timer,
                Enabled=$enabled,
                ConversationRotationEnabled=$rotationEnabled,
                NewChatStartMessage=$message,
                NewChatDelaySeconds=$newChatDelay,
                RotationCooldownSeconds=$cooldown,
                MaxConversationRotations=$maxRotations,
                ModelRoutingEnabled=$modelRouting,
                PreferredModel=$preferredModel,
                FallbackModel=$fallbackModel,
                UpdatedAt=$updatedAt
            WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$id", monitor.Id);
        command.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
        command.Parameters.AddWithValue("$replyDelay", Math.Clamp(monitor.ReplyDelaySeconds, 0, 300));
        command.Parameters.AddWithValue("$timer", Math.Clamp(monitor.TimerSeconds, 1, 60));
        command.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$message", monitor.NewChatStartMessage ?? string.Empty);
        command.Parameters.AddWithValue("$newChatDelay", Math.Clamp(monitor.NewChatDelaySeconds, 0, 600));
        command.Parameters.AddWithValue("$cooldown", Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600));
        command.Parameters.AddWithValue("$maxRotations", Math.Clamp(monitor.MaxConversationRotations, 0, 1000));
        command.Parameters.AddWithValue("$modelRouting", monitor.ModelRoutingEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$preferredModel", string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel);
        command.Parameters.AddWithValue("$fallbackModel", string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
''')

replace_once(
    "src/GPTDeskTop/UI/MainForm.cs",
    '''        if (!MonitorSettingsForm.Edit(this, _selectedMonitor)) return;\n        await _database.SaveMonitorAsync(_selectedMonitor);\n        var id = _selectedMonitor.Id;\n        await RefreshMonitorsAsync();\n        SelectMonitorRow(id);\n''',
    '''        if (!MonitorSettingsForm.Edit(this, _selectedMonitor)) return;\n        var id = _selectedMonitor.Id;\n        var updated = await _database.UpdateMonitorConfigurationAsync(_selectedMonitor);\n        if (!updated)\n        {\n            AppendActivity($"Monitor #{id}: settings were not saved because the monitor no longer exists.");\n            _selectedMonitor = null;\n            await RefreshMonitorsAsync();\n            return;\n        }\n        await RefreshMonitorsAsync();\n        SelectMonitorRow(id);\n        AppendActivity($"Monitor #{id}: operator settings saved without changing its runtime conversation binding.");\n''')

Path("tests/GPTDeskTop.RuntimeTests/MonitorConfigurationUpdateTests.cs").write_text(r'''using GPTDeskTop.Data;
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
''', encoding="utf-8")

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''- Transactional Intentional Conversation Handoff — PR #70: message-count rotation, context-limit rotation and delivery-timeout recovery now re-enumerate the verified new-chat TargetId until ChatGPT exposes the final stable `/c/{conversation-id}` URL, then atomically claim that unowned conversation from the expected old saved conversation through one immediate SQLite transaction. The transaction updates the same Monitor ID, increments RotationCount only for rotation paths, and commits rotation/success receipts together. Conflicts or unresolved stable URLs leave the old authoritative tab open and close the unclaimed new tab.\n''',
    '''- Transactional Intentional Conversation Handoff is merged on `main` (`a4f1ae5ea4e7db378d63f7f0b4049b8c8991ab6a`): message-count rotation, context-limit rotation and delivery-timeout recovery now re-enumerate the verified new-chat TargetId until ChatGPT exposes the final stable `/c/{conversation-id}` URL, then atomically claim that unowned conversation from the expected old saved conversation through one immediate SQLite transaction. The transaction updates the same Monitor ID, increments RotationCount only for rotation paths, and commits rotation/success receipts together. Conflicts or unresolved stable URLs leave the old authoritative tab open and close the unclaimed new tab.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After transactional intentional conversation handoff, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #71 is the current tracked post-1.8 task: make existing-monitor operator settings saves configuration-only so a stale settings dialog can never overwrite a newer runtime conversation binding, title, target ID or RotationCount produced by repair, recovery or intentional handoff. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #71 patch applied.")
