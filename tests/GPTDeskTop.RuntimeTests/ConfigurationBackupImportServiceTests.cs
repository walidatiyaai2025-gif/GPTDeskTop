using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupImportServiceTests
{
    [Fact]
    public async Task ImportMergesByExactUrlPreservesRuntimeIdentityHistoryAndLocalOnlyMonitors()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "old-default");

            var existing = new SavedMonitor
            {
                TabId = "LIVE-TAB-42",
                Title = "Existing title",
                Url = "https://chatgpt.com/c/existing-import-test",
                AutoReply = "old reply",
                ReplyDelaySeconds = 3,
                TimerSeconds = 1,
                Enabled = true,
                RotationCount = 7,
                PreferredModel = "Auto",
                FallbackModel = "Auto"
            };
            var existingId = await database.SaveMonitorAsync(existing);
            await database.AddLogAsync(
                "Inbound",
                "history-prompt",
                "history-response",
                "Detected",
                existingId,
                existing.TabId,
                existing.Title);

            var localOnly = new SavedMonitor
            {
                TabId = "LOCAL-ONLY-TAB",
                Title = "Local only",
                Url = "https://chatgpt.com/c/local-only-import-test",
                AutoReply = "keep me",
                RotationCount = 4
            };
            var localOnlyId = await database.SaveMonitorAsync(localOnly);

            var document = CreateDocument(
                new[] { new ConfigurationBackupSetting("DefaultAutoReply", "imported-default") },
                new[]
                {
                    BackupMonitor(
                        "Imported existing title",
                        existing.Url,
                        "imported existing reply",
                        replyDelay: 8,
                        timer: 5,
                        preferredModel: "GPT-5"),
                    BackupMonitor(
                        "Imported new monitor",
                        "https://chatgpt.com/c/new-import-test",
                        "new reply",
                        replyDelay: 9,
                        timer: 6,
                        enabled: false)
                });

            var backupPath = Path.Combine(root, "backup.json");
            await File.WriteAllTextAsync(backupPath, ConfigurationBackupService.Serialize(document));

            var service = new ConfigurationBackupImportService(database);
            var result = await service.ImportAsync(backupPath);

            Assert.Equal(1, result.SettingsApplied);
            Assert.Equal(1, result.MonitorsUpdated);
            Assert.Equal(1, result.MonitorsInserted);
            Assert.Equal("imported-default", await database.GetSettingAsync("DefaultAutoReply"));

            var monitors = await database.GetSavedMonitorsAsync();
            Assert.Equal(3, monitors.Count);

            var importedExisting = Assert.Single(monitors, x => x.Url == existing.Url);
            Assert.Equal(existingId, importedExisting.Id);
            Assert.Equal("LIVE-TAB-42", importedExisting.TabId);
            Assert.Equal(7, importedExisting.RotationCount);
            Assert.Equal("Imported existing title", importedExisting.Title);
            Assert.Equal("imported existing reply", importedExisting.AutoReply);
            Assert.Equal(8, importedExisting.ReplyDelaySeconds);
            Assert.Equal(5, importedExisting.TimerSeconds);
            Assert.Equal("GPT-5", importedExisting.PreferredModel);

            var preservedLocalOnly = Assert.Single(monitors, x => x.Id == localOnlyId);
            Assert.Equal("LOCAL-ONLY-TAB", preservedLocalOnly.TabId);
            Assert.Equal(4, preservedLocalOnly.RotationCount);
            Assert.Equal("keep me", preservedLocalOnly.AutoReply);

            var inserted = Assert.Single(monitors, x => x.Url == "https://chatgpt.com/c/new-import-test");
            Assert.True(inserted.Id > 0);
            Assert.Equal(string.Empty, inserted.TabId);
            Assert.Equal(0, inserted.RotationCount);
            Assert.False(inserted.Enabled);
            Assert.Equal("new reply", inserted.AutoReply);

            var history = await database.GetRecentLogsForMonitorAsync(existingId, 10);
            var log = Assert.Single(history);
            Assert.Equal("history-prompt", log.Prompt);
            Assert.Equal("history-response", log.Response);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task AmbiguousLocalUrlRollsBackSettingsAndMonitorChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "before-import");

            const string duplicateUrl = "https://chatgpt.com/c/duplicate-local-import-test";
            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "TAB-A",
                Title = "Duplicate A",
                Url = duplicateUrl,
                AutoReply = "reply-a",
                RotationCount = 2
            });
            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "TAB-B",
                Title = "Duplicate B",
                Url = duplicateUrl,
                AutoReply = "reply-b",
                RotationCount = 3
            });

            var plan = ConfigurationBackupImportService.CreatePlan(
                Path.Combine(root, "backup.json"),
                CreateDocument(
                    new[] { new ConfigurationBackupSetting("DefaultAutoReply", "must-roll-back") },
                    new[] { BackupMonitor("Imported duplicate", duplicateUrl, "replacement") }));

            var service = new ConfigurationBackupImportService(database);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(plan));

            Assert.Contains("more than one local monitor", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("before-import", await database.GetSettingAsync("DefaultAutoReply"));

            var monitors = await database.GetSavedMonitorsAsync();
            Assert.Equal(2, monitors.Count);
            Assert.Contains(monitors, x => x.TabId == "TAB-A" && x.AutoReply == "reply-a" && x.RotationCount == 2);
            Assert.Contains(monitors, x => x.TabId == "TAB-B" && x.AutoReply == "reply-b" && x.RotationCount == 3);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task UnsupportedSchemaIsRejectedBeforeDatabaseMutation()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "unchanged");

            var invalid = new ConfigurationBackupDocument(
                "9.0",
                DateTimeOffset.UtcNow,
                "1.8.0",
                ConfigurationBackupService.SensitivityNotice,
                new[] { new ConfigurationBackupSetting("DefaultAutoReply", "must-not-apply") },
                Array.Empty<ConfigurationBackupMonitor>(),
                ConfigurationBackupService.Exclusions);
            var path = Path.Combine(root, "unsupported.json");
            await File.WriteAllTextAsync(path, ConfigurationBackupService.Serialize(invalid));

            var service = new ConfigurationBackupImportService(database);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(path));

            Assert.Equal("unchanged", await database.GetSettingAsync("DefaultAutoReply"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void UnknownAndDuplicateSettingKeysAreRejected()
    {
        var unknown = CreateDocument(
            new[] { new ConfigurationBackupSetting("CrashCount", "999") },
            Array.Empty<ConfigurationBackupMonitor>());
        var duplicate = CreateDocument(
            new[]
            {
                new ConfigurationBackupSetting("DefaultAutoReply", "one"),
                new ConfigurationBackupSetting("DefaultAutoReply", "two")
            },
            Array.Empty<ConfigurationBackupMonitor>());

        var unknownException = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("unknown.json", unknown));
        var duplicateException = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("duplicate.json", duplicate));

        Assert.Contains("not allowed", unknownException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("more than once", duplicateException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateOrNonHttpsChatGptMonitorUrlsAreRejected()
    {
        const string url = "https://chatgpt.com/c/duplicate-backup-import-test";
        var duplicate = CreateDocument(
            Array.Empty<ConfigurationBackupSetting>(),
            new[]
            {
                BackupMonitor("One", url, "reply-one"),
                BackupMonitor("Two", url, "reply-two")
            });
        var nonHttps = CreateDocument(
            Array.Empty<ConfigurationBackupSetting>(),
            new[] { BackupMonitor("Unsafe", "http://chatgpt.com/c/not-https", "reply") });

        var duplicateException = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("duplicate-monitor.json", duplicate));
        var schemeException = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("unsafe-monitor.json", nonHttps));

        Assert.Contains("appears more than once", duplicateException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute HTTPS ChatGPT", schemeException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StrictJsonRejectsRuntimeOrFutureTopLevelFields()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var service = new ConfigurationBackupImportService(database);

            var valid = ConfigurationBackupService.Serialize(CreateDocument(
                Array.Empty<ConfigurationBackupSetting>(),
                Array.Empty<ConfigurationBackupMonitor>()));
            var tampered = valid.TrimEnd();
            tampered = tampered[..^1] + ",\n  \"CrashCount\": 44,\n  \"StoredHistory\": [\"must-not-import\"]\n}";
            var path = Path.Combine(root, "tampered.json");
            await File.WriteAllTextAsync(path, tampered);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPlanAsync(path));
            Assert.Contains("unsupported fields", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static ConfigurationBackupDocument CreateDocument(
        IReadOnlyList<ConfigurationBackupSetting> settings,
        IReadOnlyList<ConfigurationBackupMonitor> monitors)
        => new(
            ConfigurationBackupService.SchemaVersion,
            DateTimeOffset.Parse("2026-08-09T09:45:00Z"),
            "1.8.0",
            ConfigurationBackupService.SensitivityNotice,
            settings,
            monitors,
            ConfigurationBackupService.Exclusions);

    private static ConfigurationBackupMonitor BackupMonitor(
        string title,
        string url,
        string autoReply,
        int replyDelay = 3,
        int timer = 1,
        bool enabled = true,
        string preferredModel = "Auto")
        => new(
            title,
            url,
            autoReply,
            replyDelay,
            timer,
            enabled,
            true,
            "كمل",
            30,
            60,
            0,
            preferredModel != "Auto",
            preferredModel,
            "Auto");

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-config-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}