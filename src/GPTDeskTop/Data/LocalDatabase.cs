using GPTDeskTop.Models;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.Data;

public sealed class LocalDatabase
{
    private readonly string _connectionString;

    public LocalDatabase(string fileName)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(AppContext.BaseDirectory, fileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, DefaultTimeout = 5 }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;

                CREATE TABLE IF NOT EXISTS MessageLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    Prompt TEXT NOT NULL DEFAULT '',
                    Response TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS SavedMonitors (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TabId TEXT NOT NULL DEFAULT '',
                    Title TEXT NOT NULL DEFAULT '',
                    Url TEXT NOT NULL DEFAULT '',
                    AutoReply TEXT NOT NULL DEFAULT '',
                    ReplyDelaySeconds INTEGER NOT NULL DEFAULT 3,
                    TimerSeconds INTEGER NOT NULL DEFAULT 1,
                    Enabled INTEGER NOT NULL DEFAULT 1,
                    ConversationRotationEnabled INTEGER NOT NULL DEFAULT 1,
                    NewChatStartMessage TEXT NOT NULL DEFAULT 'كمل',
                    NewChatDelaySeconds INTEGER NOT NULL DEFAULT 30,
                    RotationCooldownSeconds INTEGER NOT NULL DEFAULT 60,
                    MaxConversationRotations INTEGER NOT NULL DEFAULT 0,
                    RotationCount INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ConversationRotations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MonitorId INTEGER NOT NULL,
                    OldTabId TEXT NOT NULL DEFAULT '',
                    NewTabId TEXT NOT NULL DEFAULT '',
                    Trigger TEXT NOT NULL DEFAULT '',
                    StartMessage TEXT NOT NULL DEFAULT '',
                    Timestamp TEXT NOT NULL,
                    FOREIGN KEY(MonitorId) REFERENCES SavedMonitors(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Url ON SavedMonitors(Url);
                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Enabled ON SavedMonitors(Enabled);
                CREATE INDEX IF NOT EXISTS IX_ConversationRotations_MonitorId ON ConversationRotations(MonitorId);

                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('MonitorMode', 'ChromeCDP');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultAutoReply', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMonitorDelaySeconds', '3');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMonitorTimerSeconds', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultConversationRotationEnabled', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultNewChatStartMessage', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultNewChatDelaySeconds', '30');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultRotationCooldownSeconds', '60');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMaxConversationRotations', '0');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('TimeoutRecoveryMessage', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationDurationSeconds', '8');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationSoundEnabled', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationSoundType', 'Asterisk');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('ChromeHidden', '0');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await EnsureColumnAsync(connection, "MessageLogs", "MonitorId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "MessageLogs", "TabId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "MessageLogs", "TabTitle", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "SavedMonitors", "ReplyDelaySeconds", "INTEGER NOT NULL DEFAULT 3");
        await EnsureColumnAsync(connection, "SavedMonitors", "TimerSeconds", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "SavedMonitors", "ConversationRotationEnabled", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "SavedMonitors", "NewChatStartMessage", "TEXT NOT NULL DEFAULT 'كمل'");
        await EnsureColumnAsync(connection, "SavedMonitors", "NewChatDelaySeconds", "INTEGER NOT NULL DEFAULT 30");
        await EnsureColumnAsync(connection, "SavedMonitors", "RotationCooldownSeconds", "INTEGER NOT NULL DEFAULT 60");
        await EnsureColumnAsync(connection, "SavedMonitors", "MaxConversationRotations", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "SavedMonitors", "RotationCount", "INTEGER NOT NULL DEFAULT 0");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    public async Task<long> SaveMonitorAsync(SavedMonitor monitor, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        monitor.ReplyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        monitor.TimerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        monitor.NewChatDelaySeconds = Math.Clamp(monitor.NewChatDelaySeconds, 0, 600);
        monitor.RotationCooldownSeconds = Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600);
        monitor.MaxConversationRotations = Math.Clamp(monitor.MaxConversationRotations, 0, 1000);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (monitor.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO SavedMonitors(TabId, Title, Url, AutoReply, ReplyDelaySeconds, TimerSeconds, Enabled,
                    ConversationRotationEnabled, NewChatStartMessage, NewChatDelaySeconds, RotationCooldownSeconds,
                    MaxConversationRotations, RotationCount, CreatedAt, UpdatedAt)
                VALUES($tabId, $title, $url, $autoReply, $replyDelaySeconds, $timerSeconds, $enabled,
                    $rotationEnabled, $newChatMessage, $newChatDelay, $rotationCooldown,
                    $maxRotations, $rotationCount, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        }
        else
        {
            command.CommandText = """
                UPDATE SavedMonitors
                SET TabId=$tabId, Title=$title, Url=$url, AutoReply=$autoReply,
                    ReplyDelaySeconds=$replyDelaySeconds, TimerSeconds=$timerSeconds,
                    Enabled=$enabled, ConversationRotationEnabled=$rotationEnabled,
                    NewChatStartMessage=$newChatMessage, NewChatDelaySeconds=$newChatDelay,
                    RotationCooldownSeconds=$rotationCooldown, MaxConversationRotations=$maxRotations,
                    RotationCount=$rotationCount, UpdatedAt=$updatedAt
                WHERE Id=$id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", monitor.Id);
        }

        command.Parameters.AddWithValue("$tabId", monitor.TabId ?? string.Empty);
        command.Parameters.AddWithValue("$title", monitor.Title ?? string.Empty);
        command.Parameters.AddWithValue("$url", monitor.Url ?? string.Empty);
        command.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
        command.Parameters.AddWithValue("$replyDelaySeconds", monitor.ReplyDelaySeconds);
        command.Parameters.AddWithValue("$timerSeconds", monitor.TimerSeconds);
        command.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$newChatMessage", monitor.NewChatStartMessage ?? "كمل");
        command.Parameters.AddWithValue("$newChatDelay", monitor.NewChatDelaySeconds);
        command.Parameters.AddWithValue("$rotationCooldown", monitor.RotationCooldownSeconds);
        command.Parameters.AddWithValue("$maxRotations", monitor.MaxConversationRotations);
        command.Parameters.AddWithValue("$rotationCount", monitor.RotationCount);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        monitor.Id = Convert.ToInt64(result);
        monitor.UpdatedAt = now.ToLocalTime();
        if (monitor.CreatedAt == default) monitor.CreatedAt = now.ToLocalTime();
        return monitor.Id;
    }

    public async Task<List<SavedMonitor>> GetSavedMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SavedMonitor>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TabId, Title, Url, AutoReply, ReplyDelaySeconds, TimerSeconds, Enabled,
                   ConversationRotationEnabled, NewChatStartMessage, NewChatDelaySeconds,
                   RotationCooldownSeconds, MaxConversationRotations, RotationCount, CreatedAt, UpdatedAt
            FROM SavedMonitors ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SavedMonitor
            {
                Id = reader.GetInt64(0), TabId = reader.GetString(1), Title = reader.GetString(2), Url = reader.GetString(3), AutoReply = reader.GetString(4),
                ReplyDelaySeconds = Math.Clamp(reader.GetInt32(5), 0, 300), TimerSeconds = Math.Clamp(reader.GetInt32(6), 1, 60), Enabled = reader.GetInt64(7) != 0,
                ConversationRotationEnabled = reader.GetInt64(8) != 0, NewChatStartMessage = reader.GetString(9), NewChatDelaySeconds = Math.Clamp(reader.GetInt32(10), 0, 600),
                RotationCooldownSeconds = Math.Clamp(reader.GetInt32(11), 0, 3600), MaxConversationRotations = Math.Clamp(reader.GetInt32(12), 0, 1000),
                RotationCount = Math.Max(0, reader.GetInt32(13)), CreatedAt = ParseLocal(reader.GetString(14)), UpdatedAt = ParseLocal(reader.GetString(15))
            });
        }
        return result;
    }

    public async Task DeleteMonitorAsync(long monitorId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM SavedMonitors WHERE Id=$id;"; command.Parameters.AddWithValue("$id", monitorId); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddConversationRotationAsync(long monitorId, string oldTabId, string newTabId, string trigger, string startMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ConversationRotations(MonitorId, OldTabId, NewTabId, Trigger, StartMessage, Timestamp) VALUES($monitorId,$oldTabId,$newTabId,$trigger,$startMessage,$timestamp);";
        command.Parameters.AddWithValue("$monitorId", monitorId); command.Parameters.AddWithValue("$oldTabId", oldTabId ?? string.Empty); command.Parameters.AddWithValue("$newTabId", newTabId ?? string.Empty);
        command.Parameters.AddWithValue("$trigger", trigger ?? string.Empty); command.Parameters.AddWithValue("$startMessage", startMessage ?? string.Empty); command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO AppSettings(Key, Value) VALUES($key, $value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", value ?? string.Empty); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Value FROM AppSettings WHERE Key=$key LIMIT 1;"; command.Parameters.AddWithValue("$key", key);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    public async Task<int> GetIntSettingAsync(string key, int defaultValue, int min, int max, CancellationToken cancellationToken = default)
    {
        var raw = await GetSettingAsync(key, cancellationToken);
        return int.TryParse(raw, out var value) ? Math.Clamp(value, min, max) : Math.Clamp(defaultValue, min, max);
    }

    public async Task AddLogAsync(string direction, string prompt, string response, string status, long? monitorId = null, string? tabId = null, string? tabTitle = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO MessageLogs(Timestamp, MonitorId, TabId, TabTitle, Direction, Prompt, Response, Status) VALUES ($timestamp, $monitorId, $tabId, $tabTitle, $direction, $prompt, $response, $status);";
        command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$monitorId", monitorId.HasValue ? monitorId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$tabId", tabId ?? string.Empty); command.Parameters.AddWithValue("$tabTitle", tabTitle ?? string.Empty); command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$prompt", prompt ?? string.Empty); command.Parameters.AddWithValue("$response", response ?? string.Empty); command.Parameters.AddWithValue("$status", status ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<MessageLog>> GetRecentLogsAsync(int limit = 500, CancellationToken cancellationToken = default)
    {
        var result = new List<MessageLog>();
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Id, Timestamp, MonitorId, TabId, TabTitle, Direction, Prompt, Response, Status FROM MessageLogs ORDER BY Id DESC LIMIT $limit;"; command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MessageLog { Id = reader.GetInt64(0), Timestamp = ParseLocal(reader.GetString(1)), MonitorId = reader.IsDBNull(2) ? null : reader.GetInt64(2), TabId = reader.GetString(3), TabTitle = reader.GetString(4), Direction = reader.GetString(5), Prompt = reader.GetString(6), Response = reader.GetString(7), Status = reader.GetString(8) });
        }
        return result;
    }

    public async Task DeleteLogAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM MessageLogs WHERE Id=$id;"; command.Parameters.AddWithValue("$id", id); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearLogsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM MessageLogs;"; await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTime ParseLocal(string value) => DateTime.TryParse(value, out var dt) ? dt.ToLocalTime() : DateTime.MinValue;
}
