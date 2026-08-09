using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupRoundTripSafetyTests
{
    [Fact]
    public void CreateDocumentRejectsInvalidLegacyConversationIdentity()
    {
        var monitor = PortableMonitor(41, "https://chatgpt.com/share/legacy-not-stable");

        var error = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationBackupService.CreateDocument(
                new Dictionary<string, string?>(),
                new[] { monitor },
                DateTimeOffset.UnixEpoch,
                "1.8.0"));

        Assert.Contains("does not have a stable ChatGPT conversation identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runtime Health Repair", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDocumentRejectsLogicalDuplicateOwnershipAndCanonicalizesValidUrls()
    {
        var duplicates = new[]
        {
            PortableMonitor(51, "https://chatgpt.com/c/export-duplicate"),
            PortableMonitor(52, "https://CHATGPT.com/c/export-duplicate/")
        };

        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationBackupService.CreateDocument(
                new Dictionary<string, string?>(),
                duplicates,
                DateTimeOffset.UnixEpoch,
                "1.8.0"));
        Assert.Contains("duplicate ChatGPT conversation ownership", duplicateError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runtime Health Repair", duplicateError.Message, StringComparison.OrdinalIgnoreCase);

        var document = ConfigurationBackupService.CreateDocument(
            new Dictionary<string, string?>(),
            new[] { PortableMonitor(53, "https://chatgpt.com/c/export-canonical/") },
            DateTimeOffset.UnixEpoch,
            "1.8.0");

        Assert.Equal("https://chatgpt.com/c/export-canonical", Assert.Single(document.Monitors).Url);
    }

    [Fact]
    public async Task FailedExportPreservesExistingDestinationAndLeavesNoTemporaryFile()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var outputPath = Path.Combine(root, "backup.json");
            await File.WriteAllTextAsync(outputPath, "KNOWN-GOOD-EXISTING-BACKUP");

            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            var saved = new SavedMonitor
            {
                TabId = "LEGACY-TARGET",
                Title = "Legacy invalid",
                Url = "https://chatgpt.com/c/export-invalid-source",
                AutoReply = "reply",
                Enabled = true
            };
            var monitorId = await database.SaveMonitorAsync(saved);
            await SetStoredUrlAsync(databasePath, monitorId, "https://chatgpt.com/share/export-invalid-legacy");

            var service = new ConfigurationBackupService(database);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(outputPath));

            Assert.Contains("stable ChatGPT conversation identity", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("KNOWN-GOOD-EXISTING-BACKUP", await File.ReadAllTextAsync(outputPath));
            Assert.Empty(Directory.GetFiles(root, ".*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RealDatabaseExportRefusesLegacyLogicalDuplicatesWithoutWritingDestination()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var outputPath = Path.Combine(root, "duplicate-backup.json");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            var first = new SavedMonitor
            {
                TabId = "TAB-A",
                Title = "First",
                Url = "https://chatgpt.com/c/export-legacy-duplicate",
                AutoReply = "one",
                Enabled = true
            };
            var firstId = await database.SaveMonitorAsync(first);
            await InsertLegacyDuplicateAsync(databasePath, firstId, "https://chatgpt.com/c/export-legacy-duplicate/");

            var service = new ConfigurationBackupService(database);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(outputPath));

            Assert.Contains("duplicate ChatGPT conversation ownership", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(root, ".*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static SavedMonitor PortableMonitor(long id, string url) => new()
    {
        Id = id,
        Title = $"Monitor {id}",
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

    private static async Task SetStoredUrlAsync(string databasePath, long id, string url)
    {
        await using var connection = Open(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SavedMonitors SET Url=$url WHERE Id=$id;";
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertLegacyDuplicateAsync(string databasePath, long sourceId, string url)
    {
        await using var connection = Open(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SavedMonitors(
                TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt)
            SELECT
                'TAB-B','Second',$url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt
            FROM SavedMonitors WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$id", sourceId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-config-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
