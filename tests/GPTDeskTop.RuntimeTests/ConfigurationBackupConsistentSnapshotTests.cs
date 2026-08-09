using GPTDeskTop.Data;
using GPTDeskTop.Models;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupConsistentSnapshotTests
{
    [Fact]
    public async Task SnapshotReturnsRequestedSettingsAndSavedMonitorsTogether()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingsAsync(new Dictionary<string, string>
            {
                ["DefaultAutoReply"] = "snapshot-reply",
                ["NotificationSoundEnabled"] = "0",
                ["UnrequestedKey"] = "must-not-be-returned"
            });
            await database.SaveMonitorAsync(Monitor("SNAPSHOT-TAB", "Snapshot", "https://chatgpt.com/c/backup-snapshot-basic"));

            var snapshot = await database.ReadConfigurationBackupSnapshotAsync(
                new[] { "DefaultAutoReply", "NotificationSoundEnabled", "MissingSetting" });

            Assert.Equal(3, snapshot.Settings.Count);
            Assert.Equal("snapshot-reply", snapshot.Settings["DefaultAutoReply"]);
            Assert.Equal("0", snapshot.Settings["NotificationSoundEnabled"]);
            Assert.Null(snapshot.Settings["MissingSetting"]);
            Assert.False(snapshot.Settings.ContainsKey("UnrequestedKey"));
            var monitor = Assert.Single(snapshot.Monitors);
            Assert.Equal("SNAPSHOT-TAB", monitor.TabId);
            Assert.Equal("https://chatgpt.com/c/backup-snapshot-basic", monitor.Url);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SnapshotNeverObservesUncommittedWriterAndNextSnapshotSeesCommittedPair()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "OLD-SETTING");
            var monitorId = await database.SaveMonitorAsync(
                Monitor("OLD-TAB", "Old title", "https://chatgpt.com/c/backup-snapshot-pair"));

            await using var writer = Open(databasePath);
            await writer.OpenAsync();
            using var writerTransaction = writer.BeginTransaction(deferred: false);
            await using (var settingUpdate = writer.CreateCommand())
            {
                settingUpdate.Transaction = writerTransaction;
                settingUpdate.CommandText = "UPDATE AppSettings SET Value='NEW-SETTING' WHERE Key='DefaultAutoReply';";
                Assert.Equal(1, await settingUpdate.ExecuteNonQueryAsync());
            }
            await using (var monitorUpdate = writer.CreateCommand())
            {
                monitorUpdate.Transaction = writerTransaction;
                monitorUpdate.CommandText = "UPDATE SavedMonitors SET TabId='NEW-TAB', Title='New title' WHERE Id=$id;";
                monitorUpdate.Parameters.AddWithValue("$id", monitorId);
                Assert.Equal(1, await monitorUpdate.ExecuteNonQueryAsync());
            }

            var beforeCommit = await database.ReadConfigurationBackupSnapshotAsync(new[] { "DefaultAutoReply" });
            Assert.Equal("OLD-SETTING", beforeCommit.Settings["DefaultAutoReply"]);
            Assert.Equal("OLD-TAB", Assert.Single(beforeCommit.Monitors).TabId);

            writerTransaction.Commit();

            var afterCommit = await database.ReadConfigurationBackupSnapshotAsync(new[] { "DefaultAutoReply" });
            Assert.Equal("NEW-SETTING", afterCommit.Settings["DefaultAutoReply"]);
            Assert.Equal("NEW-TAB", Assert.Single(afterCommit.Monitors).TabId);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void BackupCollectorUsesSingleDatabaseSnapshotBoundary()
    {
        var service = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupService.cs");
        var start = service.IndexOf("public async Task<ConfigurationBackupDocument> CollectAsync", StringComparison.Ordinal);
        var end = service.IndexOf("public static ConfigurationBackupDocument CreateDocument", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var collect = service[start..end];
        Assert.Contains("ReadConfigurationBackupSnapshotAsync(AllowedSettingKeys", collect, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSettingAsync", collect, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSavedMonitorsAsync", collect, StringComparison.Ordinal);

        var database = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");
        var snapshotStart = database.IndexOf("ReadConfigurationBackupSnapshotAsync", StringComparison.Ordinal);
        var snapshotEnd = database.IndexOf("public async Task<List<SavedMonitor>> GetSavedMonitorsAsync", snapshotStart, StringComparison.Ordinal);
        Assert.True(snapshotStart >= 0);
        Assert.True(snapshotEnd > snapshotStart);
        var snapshotSource = database[snapshotStart..snapshotEnd];
        Assert.Contains("Cache = SqliteCacheMode.Private", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("connection.BeginTransaction(deferred: true)", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("settingsCommand.Transaction = transaction", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("monitorsCommand.Transaction = transaction", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("transaction.Commit()", snapshotSource, StringComparison.Ordinal);
    }

    private static SavedMonitor Monitor(string tabId, string title, string url) => new()
    {
        TabId = tabId,
        Title = title,
        Url = url,
        AutoReply = "reply",
        ReplyDelaySeconds = 3,
        TimerSeconds = 1,
        Enabled = true,
        ConversationRotationEnabled = true,
        NewChatStartMessage = "كمل",
        NewChatDelaySeconds = 30,
        RotationCooldownSeconds = 60,
        PreferredModel = "Auto",
        FallbackModel = "Auto"
    };

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Shared }.ToString());

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-backup-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
