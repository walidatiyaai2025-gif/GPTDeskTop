using GPTDeskTop.Data;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class MessageLogRetentionPerformanceTests
{
    [Fact]
    public async Task HistoryMigrationAddsPlannerIndexAndBoundsRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gptdesktop-perf007-{Guid.NewGuid():N}.db");
        try
        {
            var database = new LocalDatabase(path);
            await database.InitializeAsync();
            await DatabasePerformanceInitializer.ApplyAsync(path);
            await database.SetSettingsAsync(new Dictionary<string, string>
            {
                ["MessageLogRetentionDays"] = "0",
                ["MessageLogMaxRows"] = "100",
                ["MessageLogCleanupEveryRows"] = "10"
            });

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using (var transaction = connection.BeginTransaction())
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,1,'tab','title','Inbound','','response','Detected');";
                var timestamp = insert.Parameters.Add("$ts", SqliteType.Text);
                for (var index = 0; index < 120; index++)
                {
                    timestamp.Value = DateTime.UtcNow.AddSeconds(index).ToString("O");
                    await insert.ExecuteNonQueryAsync();
                }
                transaction.Commit();
            }

            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM MessageLogs;";
                Assert.Equal(100L, Convert.ToInt64(await count.ExecuteScalarAsync()));
            }

            await using (var plan = connection.CreateCommand())
            {
                plan.CommandText = "EXPLAIN QUERY PLAN SELECT Id FROM MessageLogs WHERE MonitorId=1 ORDER BY Id DESC LIMIT 12;";
                await using var reader = await plan.ExecuteReaderAsync();
                var details = new List<string>();
                while (await reader.ReadAsync()) details.Add(reader.GetString(3));
                Assert.Contains(details, detail => detail.Contains("IX_MessageLogs_MonitorId_Id", StringComparison.Ordinal));
            }

            await using (var trigger = connection.CreateCommand())
            {
                trigger.CommandText = "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='TR_MessageLogs_Retention';";
                var sql = Convert.ToString(await trigger.ExecuteScalarAsync());
                Assert.Contains("MessageLogCleanupEveryRows", sql, StringComparison.Ordinal);
                Assert.Contains("MessageLogRetentionDays", sql, StringComparison.Ordinal);
                Assert.Contains("MessageLogMaxRows", sql, StringComparison.Ordinal);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
            try { File.Delete(path + "-wal"); } catch { }
            try { File.Delete(path + "-shm"); } catch { }
        }
    }
}
