using Microsoft.Data.Sqlite;

namespace GPTDeskTop.Data;

public static class DatabasePerformanceInitializer
{
    public const int DefaultRetentionDays = 90;
    public const int DefaultMaxRows = 50_000;
    public const int DefaultCleanupEveryRows = 250;

    public static async Task ApplyAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A SQLite database path is required.", nameof(databasePath));

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout=5000;

            CREATE INDEX IF NOT EXISTS IX_MessageLogs_MonitorId_Id
                ON MessageLogs(MonitorId, Id DESC);
            CREATE INDEX IF NOT EXISTS IX_MessageLogs_Timestamp_Id
                ON MessageLogs(Timestamp, Id);

            INSERT OR IGNORE INTO AppSettings(Key, Value)
                VALUES ('MessageLogRetentionDays', '90');
            INSERT OR IGNORE INTO AppSettings(Key, Value)
                VALUES ('MessageLogMaxRows', '50000');
            INSERT OR IGNORE INTO AppSettings(Key, Value)
                VALUES ('MessageLogCleanupEveryRows', '250');

            DROP TRIGGER IF EXISTS TR_MessageLogs_Retention;
            CREATE TRIGGER TR_MessageLogs_Retention
            AFTER INSERT ON MessageLogs
            WHEN NEW.Id % MAX(
                1,
                COALESCE(
                    (SELECT MIN(10000, MAX(10, CAST(Value AS INTEGER)))
                     FROM AppSettings
                     WHERE Key='MessageLogCleanupEveryRows'),
                    250)) = 0
            BEGIN
                DELETE FROM MessageLogs
                WHERE COALESCE(
                        (SELECT CAST(Value AS INTEGER)
                         FROM AppSettings
                         WHERE Key='MessageLogRetentionDays'),
                        90) > 0
                  AND Timestamp < strftime(
                        '%Y-%m-%dT%H:%M:%fZ',
                        'now',
                        '-' || COALESCE(
                            (SELECT MIN(3650, MAX(1, CAST(Value AS INTEGER)))
                             FROM AppSettings
                             WHERE Key='MessageLogRetentionDays'),
                            90) || ' days');

                DELETE FROM MessageLogs
                WHERE COALESCE(
                        (SELECT CAST(Value AS INTEGER)
                         FROM AppSettings
                         WHERE Key='MessageLogMaxRows'),
                        50000) > 0
                  AND Id <= COALESCE((SELECT MAX(Id) FROM MessageLogs), 0)
                      - COALESCE(
                          (SELECT MIN(500000, MAX(100, CAST(Value AS INTEGER)))
                           FROM AppSettings
                           WHERE Key='MessageLogMaxRows'),
                          50000);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
