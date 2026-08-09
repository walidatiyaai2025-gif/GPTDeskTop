using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryDuplicateOwnershipTests
{
    [Fact]
    public async Task PendingRetryNeverCreatesSendsOrStartsDuplicateConversationOwners()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "duplicate-owner-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "continue");

            const string url = "https://chatgpt.com/c/legacy-duplicate-recovery";
            var firstId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "duplicate-tab-a",
                Title = "Duplicate A",
                Url = url,
                Enabled = true
            });
            await InsertLegacyDuplicateAsync(databasePath, firstId, "duplicate-tab-b", "Duplicate B");

            var runtime = new RecordingRuntime();
            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Equal(0, runtime.CreateCalls);
            Assert.Equal(0, runtime.SendCalls);
            Assert.Equal(0, runtime.StartCalls);
            Assert.Equal(0, runtime.StopAllCalls);
            Assert.Equal(0, runtime.CloseAllCalls);
            Assert.Equal(0, runtime.GetTabsCalls);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));

            var logs = await database.GetRecentLogsAsync(50);
            var duplicateLogs = logs
                .Where(log => log.Status == "CrashRecoveryDuplicateConversationOwnership")
                .ToArray();
            Assert.Equal(2, duplicateLogs.Length);
            Assert.Equal(2, duplicateLogs.Select(log => log.MonitorId).Distinct().Count());
            Assert.Contains(logs, log => log.Status == "CrashRecoveryPartialFailure");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task UniqueOwnerStillUsesNormalPendingRetryDelivery()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "unique-owner-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "continue");

            const string url = "https://chatgpt.com/c/unique-recovery-owner";
            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "unique-tab",
                Title = "Unique",
                Url = url,
                Enabled = false
            });

            var runtime = new RecordingRuntime
            {
                Tabs =
                [
                    new ChromeTab { Id = "unique-tab", Title = "Unique", Url = url }
                ]
            };
            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Equal(1, runtime.GetTabsCalls);
            Assert.Equal(0, runtime.CreateCalls);
            Assert.Equal(1, runtime.SendCalls);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task InsertLegacyDuplicateAsync(
        string databasePath,
        long sourceMonitorId,
        string tabId,
        string title)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SavedMonitors(
                TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt)
            SELECT
                $tabId,$title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt
            FROM SavedMonitors WHERE Id=$sourceMonitorId;
            """;
        command.Parameters.AddWithValue("$tabId", tabId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$sourceMonitorId", sourceMonitorId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed class RecordingRuntime : ICrashRecoveryRuntime
    {
        public IReadOnlyList<ChromeTab> Tabs { get; init; } = Array.Empty<ChromeTab>();
        public int StopAllCalls { get; private set; }
        public int CloseAllCalls { get; private set; }
        public int GetTabsCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int SendCalls { get; private set; }
        public int StartCalls { get; private set; }

        public Task StopAllMonitorsAsync()
        {
            StopAllCalls++;
            return Task.CompletedTask;
        }

        public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken)
        {
            CloseAllCalls++;
            return Task.CompletedTask;
        }

        public void LaunchMonitorChrome(string? startUrl) { }

        public Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
        {
            GetTabsCalls++;
            return Task.FromResult(Tabs);
        }

        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new ChromeTab { Id = $"created-{CreateCalls}", Title = "Created", Url = url });
        }

        public Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(true);
        }

        public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
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