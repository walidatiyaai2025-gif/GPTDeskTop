using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.RuntimeTests;

public sealed class CanonicalConfigurationImportOwnershipTests
{
    [Fact]
    public void CreatePlanRejectsLogicalDuplicateMonitorUrlsAndCanonicalizesAcceptedUrls()
    {
        var duplicate = Document(
            Array.Empty<ConfigurationBackupSetting>(),
            new[]
            {
                Monitor("One", "https://chatgpt.com/c/canonical-import-duplicate", "one"),
                Monitor("Two", "https://chatgpt.com/c/canonical-import-duplicate/", "two")
            });

        var error = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("duplicate.json", duplicate));
        Assert.Contains("appears more than once", error.Message, StringComparison.OrdinalIgnoreCase);

        var accepted = ConfigurationBackupImportService.CreatePlan(
            "accepted.json",
            Document(
                Array.Empty<ConfigurationBackupSetting>(),
                new[] { Monitor("Accepted", "https://chatgpt.com/c/canonical-import-accepted/", "reply") }));
        Assert.Equal("https://chatgpt.com/c/canonical-import-accepted", Assert.Single(accepted.Monitors).Url);
    }

    [Fact]
    public async Task LogicalLocalOwnerIsUpdatedWithoutChangingLegacyRuntimeBindingOrStoredUrl()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();

            var saved = new SavedMonitor
            {
                TabId = "LIVE-TARGET",
                Title = "Before",
                Url = "https://chatgpt.com/c/canonical-import-existing",
                AutoReply = "before",
                RotationCount = 11
            };
            var id = await database.SaveMonitorAsync(saved);
            const string legacyStoredUrl = "https://chatgpt.com/c/canonical-import-existing/";
            await SetStoredUrlAsync(databasePath, id, legacyStoredUrl);

            var service = new ConfigurationBackupImportService(database);
            var plan = ConfigurationBackupImportService.CreatePlan(
                Path.Combine(root, "backup.json"),
                Document(
                    new[] { new ConfigurationBackupSetting("DefaultAutoReply", "imported-default") },
                    new[] { Monitor("After", "https://CHATGPT.com/c/canonical-import-existing", "after") }));

            var result = await service.ApplyAsync(plan);

            Assert.Equal(1, result.SettingsApplied);
            Assert.Equal(1, result.MonitorsUpdated);
            Assert.Equal(0, result.MonitorsInserted);
            var current = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal(id, current.Id);
            Assert.Equal("LIVE-TARGET", current.TabId);
            Assert.Equal(legacyStoredUrl, current.Url);
            Assert.Equal(11, current.RotationCount);
            Assert.Equal("After", current.Title);
            Assert.Equal("after", current.AutoReply);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task AmbiguousLegacyLogicalOwnersRollBackSettingsAndMonitorChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var databasePath = Path.Combine(root, "test.db");
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "before-import");

            var first = new SavedMonitor
            {
                TabId = "TAB-A",
                Title = "A",
                Url = "https://chatgpt.com/c/canonical-import-ambiguous",
                AutoReply = "reply-a",
                RotationCount = 2
            };
            var firstId = await database.SaveMonitorAsync(first);
            await InsertLegacyVariantDuplicateAsync(
                databasePath,
                firstId,
                "https://chatgpt.com/c/canonical-import-ambiguous/",
                "TAB-B",
                "B",
                "reply-b",
                3);

            var plan = ConfigurationBackupImportService.CreatePlan(
                Path.Combine(root, "backup.json"),
                Document(
                    new[] { new ConfigurationBackupSetting("DefaultAutoReply", "must-roll-back") },
                    new[] { Monitor("Imported", "https://chatgpt.com/c/canonical-import-ambiguous", "replacement") }));

            var service = new ConfigurationBackupImportService(database);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(plan));

            Assert.Contains("more than one local monitor", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("before-import", await database.GetSettingAsync("DefaultAutoReply"));
            var current = await database.GetSavedMonitorsAsync();
            Assert.Equal(2, current.Count);
            Assert.Contains(current, x => x.TabId == "TAB-A" && x.AutoReply == "reply-a" && x.RotationCount == 2);
            Assert.Contains(current, x => x.TabId == "TAB-B" && x.AutoReply == "reply-b" && x.RotationCount == 3);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DatabaseBoundaryRejectsLogicalDuplicatePayloadBeforeSettingsMutation()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "unchanged");

            var monitors = new[]
            {
                Saved("https://chatgpt.com/c/canonical-import-boundary", "one"),
                Saved("https://chatgpt.com/c/canonical-import-boundary/", "two")
            };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.ApplyConfigurationImportAsync(
                    new Dictionary<string, string> { ["DefaultAutoReply"] = "must-not-apply" },
                    monitors));

            Assert.Contains("same logical ChatGPT conversation", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", await database.GetSettingAsync("DefaultAutoReply"));
            Assert.Empty(await database.GetSavedMonitorsAsync());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task NewImportedMonitorPersistsCanonicalUrlWithFreshRuntimeState()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var service = new ConfigurationBackupImportService(database);
            var plan = ConfigurationBackupImportService.CreatePlan(
                Path.Combine(root, "backup.json"),
                Document(
                    Array.Empty<ConfigurationBackupSetting>(),
                    new[] { Monitor("New", "https://chatgpt.com/c/canonical-import-new/", "reply") }));

            var result = await service.ApplyAsync(plan);

            Assert.Equal(0, result.MonitorsUpdated);
            Assert.Equal(1, result.MonitorsInserted);
            var inserted = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal("https://chatgpt.com/c/canonical-import-new", inserted.Url);
            Assert.Equal(string.Empty, inserted.TabId);
            Assert.Equal(0, inserted.RotationCount);
            Assert.True(inserted.Id > 0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static SavedMonitor Saved(string url, string reply) => new()
    {
        Title = "Imported",
        Url = url,
        AutoReply = reply,
        ReplyDelaySeconds = 3,
        TimerSeconds = 1,
        Enabled = true,
        ConversationRotationEnabled = true,
        NewChatStartMessage = "كمل",
        NewChatDelaySeconds = 30,
        RotationCooldownSeconds = 60,
        MaxConversationRotations = 0,
        PreferredModel = "Auto",
        FallbackModel = "Auto"
    };

    private static ConfigurationBackupDocument Document(
        IReadOnlyList<ConfigurationBackupSetting> settings,
        IReadOnlyList<ConfigurationBackupMonitor> monitors)
        => new(
            ConfigurationBackupService.SchemaVersion,
            DateTimeOffset.Parse("2026-08-09T18:00:00Z"),
            "1.8.0",
            ConfigurationBackupService.SensitivityNotice,
            settings,
            monitors,
            ConfigurationBackupService.Exclusions);

    private static ConfigurationBackupMonitor Monitor(string title, string url, string reply)
        => new(title, url, reply, 3, 1, true, true, "كمل", 30, 60, 0, false, "Auto", "Auto");

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

    private static async Task InsertLegacyVariantDuplicateAsync(
        string databasePath,
        long sourceMonitorId,
        string url,
        string tabId,
        string title,
        string reply,
        int rotationCount)
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
                $tabId,$title,$url,$reply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,$rotationCount,
                ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt
            FROM SavedMonitors WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$tabId", tabId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$reply", reply);
        command.Parameters.AddWithValue("$rotationCount", rotationCount);
        command.Parameters.AddWithValue("$id", sourceMonitorId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-canonical-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
