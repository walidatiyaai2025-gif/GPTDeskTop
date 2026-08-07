using GPTDeskTop.Models;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.Data;

public sealed class LocalDatabase
{
    private readonly string _connectionString;

    public LocalDatabase(string fileName)
    {
        var path = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(AppContext.BaseDirectory, fileName);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();
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
                    Enabled INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Url ON SavedMonitors(Url);
                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Enabled ON SavedMonitors(Enabled);

                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('MonitorMode', 'ChromeCDP');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultAutoReply', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationDurationSeconds', '8');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('ReplyDelaySeconds', '3');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await EnsureColumnAsync(connection, "MessageLogs", "MonitorId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "MessageLogs", "TabId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "MessageLogs", "TabTitle", "TEXT NOT NULL DEFAULT ''");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        await reader.DisposeAsync();

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    public async Task<long> SaveMonitorAsync(SavedMonitor monitor, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (monitor.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO SavedMonitors(TabId, Title, Url, AutoReply, Enabled, CreatedAt, UpdatedAt)
                VALUES($tabId, $title, $url, $autoReply, $enabled, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        }
        else
        {
            command.CommandText = """
                UPDATE SavedMonitors
                SET TabId=$tabId, Title=$title, Url=$url, AutoReply=$autoReply,
                    Enabled=$enabled, UpdatedAt=$updatedAt
                WHERE Id=$id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", monitor.Id);
        }

        command.Parameters.AddWithValue("$tabId", monitor.TabId ?? string.Empty);
        command.Parameters.AddWithValue("$title", monitor.Title ?? string.Empty);
        command.Parameters.AddWithValue("$url", monitor.Url ?? string.Empty);
        command.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
        command.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        monitor.Id = Convert.ToInt64(result);
        monitor.UpdatedAt = now.ToLocalTime();
        if (monitor.CreatedAt == default)
            monitor.CreatedAt = now.ToLocalTime();
        return monitor.Id;
    }

    public async Task<List<SavedMonitor>> GetSavedMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SavedMonitor>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TabId, Title, Url, AutoReply, Enabled, CreatedAt, UpdatedAt
            FROM SavedMonitors
            ORDER BY Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SavedMonitor
            {
                Id = reader.GetInt64(0),
                TabId = reader.GetString(1),
                Title = reader.GetString(2),
                Url = reader.GetString(3),
                AutoReply = reader.GetString(4),
                Enabled = reader.GetInt64(5) != 0,
                CreatedAt = ParseLocal(reader.GetString(6)),
                UpdatedAt = ParseLocal(reader.GetString(7))
            });
        }
        return result;
    }

    public async Task DeleteMonitorAsync(long monitorId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SavedMonitors WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", monitorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(Key, Value) VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    public async Task<int> GetIntSettingAsync(string key, int defaultValue, int min, int max, CancellationToken cancellationToken = default)
    {
        var raw = await GetSettingAsync(key, cancellationToken);
        return int.TryParse(raw, out var value) ? Math.Clamp(value, min, max) : Math.Clamp(defaultValue, min, max);
    }

    public async Task AddLogAsync(
        string direction,
        string prompt,
        string response,
        string status,
        long? monitorId = null,
        string? tabId = null,
        string? tabTitle = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MessageLogs(Timestamp, MonitorId, TabId, TabTitle, Direction, Prompt, Response, Status)
            VALUES ($timestamp, $monitorId, $tabId, $tabTitle, $direction, $prompt, $response, $status);
            """;
        command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$monitorId", monitorId.HasValue ? monitorId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$tabId", tabId ?? string.Empty);
        command.Parameters.AddWithValue("$tabTitle", tabTitle ?? string.Empty);
        command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$prompt", prompt ?? string.Empty);
        command.Parameters.AddWithValue("$response", response ?? string.Empty);
        command.Parameters.AddWithValue("$status", status ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<MessageLog>> GetRecentLogsAsync(int limit = 500, CancellationToken cancellationToken = default)
    {
        var result = new List<MessageLog>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Timestamp, MonitorId, TabId, TabTitle, Direction, Prompt, Response, Status
            FROM MessageLogs
            ORDER BY Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MessageLog
            {
                Id = reader.GetInt64(0),
                Timestamp = ParseLocal(reader.GetString(1)),
                MonitorId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                TabId = reader.GetString(3),
                TabTitle = reader.GetString(4),
                Direction = reader.GetString(5),
                Prompt = reader.GetString(6),
                Response = reader.GetString(7),
                Status = reader.GetString(8)
            });
        }
        return result;
    }

    public async Task DeleteLogAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MessageLogs WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearLogsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MessageLogs;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTime ParseLocal(string value)
        => DateTime.TryParse(value, out var dt) ? dt.ToLocalTime() : DateTime.MinValue;
}
