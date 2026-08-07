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
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = """
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

            INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('MonitorMode', 'ChromeCDP');
            INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultAutoReply', 'كمل');
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddLogAsync(string direction, string prompt, string response, string status, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MessageLogs(Timestamp, Direction, Prompt, Response, Status)
            VALUES ($timestamp, $direction, $prompt, $response, $status);
            """;
        command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
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
            SELECT Id, Timestamp, Direction, Prompt, Response, Status
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
                Timestamp = DateTime.TryParse(reader.GetString(1), out var dt) ? dt.ToLocalTime() : DateTime.MinValue,
                Direction = reader.GetString(2),
                Prompt = reader.GetString(3),
                Response = reader.GetString(4),
                Status = reader.GetString(5)
            });
        }

        return result;
    }
}
