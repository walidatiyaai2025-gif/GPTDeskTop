from pathlib import Path

# LocalDatabase: one connection + one read transaction for backup settings and monitors.
db_path = Path('src/GPTDeskTop/Data/LocalDatabase.cs')
db = db_path.read_text(encoding='utf-8')
record_anchor = 'public sealed record ConfigurationImportDatabaseResult(int SettingsApplied, int MonitorsUpdated, int MonitorsInserted);\n'
record = 'public sealed record ConfigurationBackupDatabaseSnapshot(IReadOnlyDictionary<string, string?> Settings, IReadOnlyList<SavedMonitor> Monitors);\n'
if record not in db:
    if record_anchor not in db:
        raise RuntimeError('LocalDatabase record anchor not found')
    db = db.replace(record_anchor, record_anchor + record, 1)

method_anchor = '    public async Task<List<SavedMonitor>> GetSavedMonitorsAsync(CancellationToken cancellationToken = default)\n'
if method_anchor not in db:
    raise RuntimeError('GetSavedMonitorsAsync anchor not found')
snapshot_method = r'''    public async Task<ConfigurationBackupDatabaseSnapshot> ReadConfigurationBackupSnapshotAsync(
        IReadOnlyList<string> settingKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingKeys);
        var keys = settingKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var settings = keys.ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        var monitors = new List<SavedMonitor>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            if (keys.Length > 0)
            {
                await using var settingsCommand = connection.CreateCommand();
                settingsCommand.Transaction = transaction;
                var parameterNames = new string[keys.Length];
                for (var index = 0; index < keys.Length; index++)
                {
                    parameterNames[index] = $"$key{index}";
                    settingsCommand.Parameters.AddWithValue(parameterNames[index], keys[index]);
                }
                settingsCommand.CommandText = $"SELECT Key, Value FROM AppSettings WHERE Key IN ({string.Join(',', parameterNames)});";
                await using var settingsReader = await settingsCommand.ExecuteReaderAsync(cancellationToken);
                while (await settingsReader.ReadAsync(cancellationToken))
                    settings[settingsReader.GetString(0)] = settingsReader.GetString(1);
            }

            await using var monitorsCommand = connection.CreateCommand();
            monitorsCommand.Transaction = transaction;
            monitorsCommand.CommandText = "SELECT Id,TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt FROM SavedMonitors ORDER BY Id;";
            await using var monitorReader = await monitorsCommand.ExecuteReaderAsync(cancellationToken);
            while (await monitorReader.ReadAsync(cancellationToken))
            {
                monitors.Add(new SavedMonitor
                {
                    Id = monitorReader.GetInt64(0),
                    TabId = monitorReader.GetString(1),
                    Title = monitorReader.GetString(2),
                    Url = monitorReader.GetString(3),
                    AutoReply = monitorReader.GetString(4),
                    ReplyDelaySeconds = Math.Clamp(monitorReader.GetInt32(5), 0, 300),
                    TimerSeconds = Math.Clamp(monitorReader.GetInt32(6), 1, 60),
                    Enabled = monitorReader.GetInt64(7) != 0,
                    ConversationRotationEnabled = monitorReader.GetInt64(8) != 0,
                    NewChatStartMessage = monitorReader.GetString(9),
                    NewChatDelaySeconds = Math.Clamp(monitorReader.GetInt32(10), 0, 600),
                    RotationCooldownSeconds = Math.Clamp(monitorReader.GetInt32(11), 0, 3600),
                    MaxConversationRotations = Math.Clamp(monitorReader.GetInt32(12), 0, 1000),
                    RotationCount = Math.Max(0, monitorReader.GetInt32(13)),
                    ModelRoutingEnabled = monitorReader.GetInt64(14) != 0,
                    PreferredModel = monitorReader.GetString(15),
                    FallbackModel = monitorReader.GetString(16),
                    CreatedAt = ParseLocal(monitorReader.GetString(17)),
                    UpdatedAt = ParseLocal(monitorReader.GetString(18))
                });
            }

            transaction.Commit();
            return new ConfigurationBackupDatabaseSnapshot(settings, monitors);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

'''
db = db.replace(method_anchor, snapshot_method + method_anchor, 1)
db_path.write_text(db, encoding='utf-8')

# ConfigurationBackupService: one snapshot call, no per-key/separate monitor reads.
service_path = Path('src/GPTDeskTop/Services/ConfigurationBackupService.cs')
service = service_path.read_text(encoding='utf-8')
old_collect = r'''    public async Task<ConfigurationBackupDocument> CollectAsync(CancellationToken cancellationToken = default)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in AllowedSettingKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            settings[key] = await _database.GetSettingAsync(key, cancellationToken).ConfigureAwait(false);
        }

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        return CreateDocument(
            settings,
            monitors,
            DateTimeOffset.UtcNow,
            GetAppVersion());
    }
'''
new_collect = r'''    public async Task<ConfigurationBackupDocument> CollectAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _database
            .ReadConfigurationBackupSnapshotAsync(AllowedSettingKeys, cancellationToken)
            .ConfigureAwait(false);
        return CreateDocument(
            snapshot.Settings,
            snapshot.Monitors,
            DateTimeOffset.UtcNow,
            GetAppVersion());
    }
'''
if service.count(old_collect) != 1:
    raise RuntimeError(f'CollectAsync anchor count={service.count(old_collect)}')
service = service.replace(old_collect, new_collect, 1)
service_path.write_text(service, encoding='utf-8')

# Deterministic real-SQLite + source-contract tests.
test_path = Path('tests/GPTDeskTop.RuntimeTests/ConfigurationBackupConsistentSnapshotTests.cs')
test_path.write_text(r'''using GPTDeskTop.Data;
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
        Assert.Contains("connection.BeginTransaction()", snapshotSource, StringComparison.Ordinal);
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
''', encoding='utf-8')

# Development status: reconcile #80 merge and track #81.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Atomic Operator Settings Save is implemented in PR #80:',
    '- Atomic Operator Settings Save is merged on `main` (`8cb6871acfb8993d5c1670f044889b4e95eaaaa4`):',
    1)
active = '- Consistent Configuration Backup Snapshot is implemented for Issue #81: backup collection now reads its allowlisted settings and saved monitors through one SQLite connection and one read transaction, so the generated portable document cannot mix settings and monitor state from separate database snapshots. Existing single-key/settings/monitor read APIs remain available for unrelated callers.'
if active not in status:
    anchor_status = '- Atomic Operator Settings Save is merged on `main` (`8cb6871acfb8993d5c1670f044889b4e95eaaaa4`):'
    pos = status.index(anchor_status)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After atomic operator Settings persistence, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #81 is the current tracked post-1.8 task: collect configuration backup settings and saved monitors from one SQLite read snapshot so a concurrent settings save, repair, recovery update or handoff cannot produce a mixed-time portable backup. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected post-#79 Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #81 consistent backup snapshot patch applied.')
