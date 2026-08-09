using GPTDeskTop.Data;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class AtomicOperatorSettingsSaveTests
{
    [Fact]
    public async Task BatchSavePersistsEverySettingTogether()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            await database.SetSettingsAsync(new Dictionary<string, string>
            {
                ["DefaultAutoReply"] = "atomic-reply",
                ["DefaultMonitorDelaySeconds"] = "17",
                ["DefaultMonitorTimerSeconds"] = "9",
                ["NotificationSoundEnabled"] = "0"
            });

            Assert.Equal("atomic-reply", await database.GetSettingAsync("DefaultAutoReply"));
            Assert.Equal("17", await database.GetSettingAsync("DefaultMonitorDelaySeconds"));
            Assert.Equal("9", await database.GetSettingAsync("DefaultMonitorTimerSeconds"));
            Assert.Equal("0", await database.GetSettingAsync("NotificationSoundEnabled"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ForcedMidBatchSqliteFailureRollsBackEarlierSettingWrites()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "before-reply");
            await database.SetSettingAsync("NotificationSoundEnabled", "1");

            await using (var connection = Open(databasePath))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER FailAtomicSettingsBatch
                    BEFORE UPDATE OF Value ON AppSettings
                    WHEN OLD.Key='NotificationSoundEnabled' AND NEW.Value='0'
                    BEGIN
                        SELECT RAISE(ABORT, 'forced atomic settings failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            var error = await Assert.ThrowsAnyAsync<SqliteException>(() =>
                database.SetSettingsAsync(new Dictionary<string, string>
                {
                    ["DefaultAutoReply"] = "must-roll-back",
                    ["NotificationSoundEnabled"] = "0"
                }));

            Assert.Contains("forced atomic settings failure", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("before-reply", await database.GetSettingAsync("DefaultAutoReply"));
            Assert.Equal("1", await database.GetSettingAsync("NotificationSoundEnabled"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CompetingBatchSavesCannotLeaveMixedPairState()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var first = database.SetSettingsAsync(new Dictionary<string, string>
            {
                ["AtomicPairOne"] = "A",
                ["AtomicPairTwo"] = "A"
            });
            var second = database.SetSettingsAsync(new Dictionary<string, string>
            {
                ["AtomicPairOne"] = "B",
                ["AtomicPairTwo"] = "B"
            });

            await Task.WhenAll(first, second);
            var one = await database.GetSettingAsync("AtomicPairOne");
            var two = await database.GetSettingAsync("AtomicPairTwo");
            Assert.Equal(one, two);
            Assert.Contains(one, new[] { "A", "B" });
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void SettingsDialogUsesOneBatchWriteInsteadOfIndependentSettingWrites()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var start = source.IndexOf("private async Task SaveSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ExportConfigurationBackupAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var saveSource = source[start..end];

        Assert.Contains("new Dictionary<string, string>(StringComparer.Ordinal)", saveSource, StringComparison.Ordinal);
        Assert.Contains("await _database.SetSettingsAsync(desiredSettings);", saveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync(", saveSource, StringComparison.Ordinal);
        Assert.Contains("No partial settings changes were committed", saveSource, StringComparison.Ordinal);

        var databaseSource = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");
        Assert.Contains("public async Task SetSettingsAsync(", databaseSource, StringComparison.Ordinal);
        Assert.Contains("connection.BeginTransaction(deferred: false)", databaseSource, StringComparison.Ordinal);
        Assert.Contains("transaction.Rollback()", databaseSource, StringComparison.Ordinal);
        Assert.Contains("public async Task SetSettingAsync(", databaseSource, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-atomic-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
