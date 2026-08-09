from pathlib import Path

# LocalDatabase: add one transactional batch settings primitive while preserving single-key API.
db_path = Path('src/GPTDeskTop/Data/LocalDatabase.cs')
db = db_path.read_text(encoding='utf-8')
anchor = '''    public async Task SetSettingAsync(string key,string value,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;"; command.Parameters.AddWithValue("$key",key); command.Parameters.AddWithValue("$value",value??""); await command.ExecuteNonQueryAsync(cancellationToken); }
'''
if db.count(anchor) != 1:
    raise RuntimeError(f'SetSettingAsync anchor count={db.count(anchor)}')
batch = r'''    public async Task SetSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count == 0) return;

        foreach (var pair in settings)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Settings batch cannot contain an empty key.", nameof(settings));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            foreach (var pair in settings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
                command.Parameters.AddWithValue("$key", pair.Key);
                command.Parameters.AddWithValue("$value", pair.Value ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

'''
db = db.replace(anchor, batch + anchor, 1)
db_path.write_text(db, encoding='utf-8')

# SettingsForm: validate/materialize complete settings set before exactly one database call.
settings_path = Path('src/GPTDeskTop/UI/SettingsForm.cs')
settings_source = settings_path.read_text(encoding='utf-8')
start = settings_source.index('    private async Task SaveSettingsAsync()')
end = settings_source.index('    private async Task ExportConfigurationBackupAsync()', start)
new_method = r'''    private async Task SaveSettingsAsync()
    {
        if (_busy) return;

        var rawRotationStartMessage = _messageCountRotationStartMessage.Text.Trim();
        if (_rotateAfterMessages.Value > 0 && string.IsNullOrWhiteSpace(rawRotationStartMessage))
        {
            _tabs.SelectedIndex = 1;
            _messageCountRotationStartMessage.Focus();
            MessageBox.Show(this, "New Chat start message cannot be empty when message-count rotation is enabled.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var rotationStartMessage = string.IsNullOrWhiteSpace(rawRotationStartMessage) ? "كمل" : rawRotationStartMessage;
        var desiredSettings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DefaultAutoReply"] = string.IsNullOrWhiteSpace(_defaultReply.Text) ? "كمل" : _defaultReply.Text.Trim(),
            ["DefaultMonitorDelaySeconds"] = ((int)_defaultDelay.Value).ToString(),
            ["DefaultMonitorTimerSeconds"] = ((int)_defaultTimer.Value).ToString(),
            ["RotateAfterAssistantMessages"] = ((int)_rotateAfterMessages.Value).ToString(),
            ["MessageCountRotationStartMessage"] = rotationStartMessage,
            ["NoResponseRefreshSeconds"] = ((int)_noResponseRefresh.Value).ToString(),
            ["TimeoutRecoveryMessage"] = string.IsNullOrWhiteSpace(_timeoutRecovery.Text) ? "كمل" : _timeoutRecovery.Text.Trim(),
            ["NotificationDurationSeconds"] = ((int)_notificationDuration.Value).ToString(),
            ["NotificationSoundEnabled"] = _soundEnabled.Checked ? "1" : "0",
            ["NotificationSoundType"] = _soundType.SelectedItem?.ToString() ?? "Asterisk"
        };

        SetBusy(true, "Saving settings…");
        try
        {
            await _database.SetSettingsAsync(desiredSettings);
            _statusLabel.Text = "Settings saved.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, "Settings were not saved. No settings changes were committed; review the error and try again.");
            MessageBox.Show(this, $"GPTDeskTop could not save application settings. No partial settings changes were committed.\n\n{ex.Message}", "Settings Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

'''
settings_source = settings_source[:start] + new_method + settings_source[end:]
settings_path.write_text(settings_source, encoding='utf-8')

# Deterministic real-SQLite + source-contract coverage.
test_path = Path('tests/GPTDeskTop.RuntimeTests/AtomicOperatorSettingsSaveTests.cs')
test_path.write_text(r'''using GPTDeskTop.Data;
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
''', encoding='utf-8')

# Development status: reconcile #78 merge and track #79.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Configuration Backup Round-Trip Safety is implemented in PR #78:',
    '- Configuration Backup Round-Trip Safety is merged on `main` (`02d2d5b79be3f3254bda659ff1fd530238085b0c`):',
    1)
active = '- Atomic Operator Settings Save is implemented for Issue #79: the Settings dialog now materializes the complete validated operator settings set and commits it through one immediate SQLite writer transaction. A failed batch rolls back every setting, concurrent batches serialize without mixed pair state, and the existing single-key `SetSettingAsync` remains available for unrelated runtime operations.'
if active not in status:
    anchor_status = '- Configuration Backup Round-Trip Safety is merged on `main` (`02d2d5b79be3f3254bda659ff1fd530238085b0c`):'
    pos = status.index(anchor_status)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After configuration-backup round-trip safety, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #79 is the current tracked post-1.8 task: make operator Settings save atomic so a database failure cannot leave a partially applied set of coupled settings. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected post-#77 Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #79 atomic settings patch applied.')
