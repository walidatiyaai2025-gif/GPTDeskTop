using System.Text.Json;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupServiceTests
{
    [Fact]
    public void ProjectionUsesAllowlistAndDropsRuntimeIdentity()
    {
        var settings = new Dictionary<string, string?>
        {
            ["NotificationSoundType"] = "Beep",
            ["DefaultAutoReply"] = "continue-private-template",
            ["RotateAfterAssistantMessages"] = "25",
            ["CrashCount"] = "77",
            ["CrashRecoveryPending"] = "1",
            ["LastShutdownClean"] = "0",
            ["ChromeHidden"] = "1",
            ["Ui.RuntimeHealth.Expanded"] = "1",
            ["UnrecognizedFutureSetting"] = "must-not-export"
        };
        var monitor = new SavedMonitor
        {
            Id = 73,
            TabId = "RUNTIME-TAB-ID-MUST-NOT-EXPORT",
            Title = "Private monitor title",
            Url = "https://chatgpt.com/c/private-conversation",
            AutoReply = "private auto reply",
            ReplyDelaySeconds = 999,
            TimerSeconds = 0,
            Enabled = true,
            ConversationRotationEnabled = true,
            NewChatStartMessage = "private new chat message",
            NewChatDelaySeconds = 9999,
            RotationCooldownSeconds = 9999,
            MaxConversationRotations = 9999,
            RotationCount = 888,
            ModelRoutingEnabled = true,
            PreferredModel = "GPT-5",
            FallbackModel = "Auto",
            CreatedAt = new DateTime(2020, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };

        var document = ConfigurationBackupService.CreateDocument(
            settings,
            new[] { monitor },
            DateTimeOffset.Parse("2026-08-09T09:30:00Z"),
            "1.8.0");
        var json = ConfigurationBackupService.Serialize(document);

        Assert.Equal("1.0", document.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-08-09T09:30:00Z"), document.ExportedAtUtc);
        Assert.Equal("1.8.0", document.AppVersion);
        Assert.Equal(
            new[] { "DefaultAutoReply", "RotateAfterAssistantMessages", "NotificationSoundType" },
            document.Settings.Select(item => item.Key).ToArray());

        var exportedMonitor = Assert.Single(document.Monitors);
        Assert.Equal("Private monitor title", exportedMonitor.Title);
        Assert.Equal("https://chatgpt.com/c/private-conversation", exportedMonitor.Url);
        Assert.Equal("private auto reply", exportedMonitor.AutoReply);
        Assert.Equal(300, exportedMonitor.ReplyDelaySeconds);
        Assert.Equal(1, exportedMonitor.TimerSeconds);
        Assert.Equal(600, exportedMonitor.NewChatDelaySeconds);
        Assert.Equal(3600, exportedMonitor.RotationCooldownSeconds);
        Assert.Equal(1000, exportedMonitor.MaxConversationRotations);

        Assert.Contains("Private monitor title", json, StringComparison.Ordinal);
        Assert.Contains("private auto reply", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RUNTIME-TAB-ID-MUST-NOT-EXPORT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashRecoveryPending", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LastShutdownClean", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeHidden", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.RuntimeHealth.Expanded", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UnrecognizedFutureSetting", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RotationCount\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CreatedAt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"UpdatedAt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TabId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionKeepsAllowlistedSettingsInStableSchemaOrder()
    {
        var settings = new Dictionary<string, string?>
        {
            ["NotificationDurationSeconds"] = "8",
            ["DefaultMonitorTimerSeconds"] = "4",
            ["DefaultAutoReply"] = "كمل",
            ["HandoffMaxChars"] = null,
            ["DefaultMonitorDelaySeconds"] = "3"
        };

        var document = ConfigurationBackupService.CreateDocument(
            settings,
            Array.Empty<SavedMonitor>(),
            DateTimeOffset.UnixEpoch,
            "1.8.0");

        Assert.Equal(
            new[]
            {
                "DefaultAutoReply",
                "DefaultMonitorDelaySeconds",
                "DefaultMonitorTimerSeconds",
                "NotificationDurationSeconds"
            },
            document.Settings.Select(item => item.Key).ToArray());
        Assert.Empty(document.Monitors);
        Assert.Contains("conversation URLs", document.SensitivityNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Exclusions, item => item.Contains("runtime Chrome Tab IDs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportFromRealDatabaseCreatesJsonWithoutRuntimeOrHistoryState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gptdesktop-config-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "test.db");
        var outputPath = Path.Combine(root, "backup.json");

        try
        {
            var database = new LocalDatabase(databasePath);
            await database.InitializeAsync();
            await database.SetSettingAsync("DefaultAutoReply", "real-db-template");
            await database.SetSettingAsync("CrashCount", "12345");
            await database.AddLogAsync(
                "Inbound",
                "HISTORY-PROMPT-MUST-NOT-EXPORT",
                "HISTORY-RESPONSE-MUST-NOT-EXPORT",
                "Detected");
            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "REAL-RUNTIME-TAB-ID",
                Title = "Portable monitor",
                Url = "https://chatgpt.com/c/portable-conversation",
                AutoReply = "portable reply",
                Enabled = true,
                RotationCount = 91
            });

            var service = new ConfigurationBackupService(database);
            var created = await service.ExportAsync(outputPath);
            var json = await File.ReadAllTextAsync(created);
            var parsed = JsonSerializer.Deserialize<ConfigurationBackupDocument>(json);

            Assert.Equal(outputPath, created);
            Assert.NotNull(parsed);
            Assert.Equal("1.0", parsed!.SchemaVersion);
            Assert.Single(parsed.Monitors);
            Assert.Contains("real-db-template", json, StringComparison.Ordinal);
            Assert.Contains("Portable monitor", json, StringComparison.Ordinal);
            Assert.DoesNotContain("REAL-RUNTIME-TAB-ID", json, StringComparison.Ordinal);
            Assert.DoesNotContain("CrashCount", json, StringComparison.Ordinal);
            Assert.DoesNotContain("12345", json, StringComparison.Ordinal);
            Assert.DoesNotContain("HISTORY-PROMPT-MUST-NOT-EXPORT", json, StringComparison.Ordinal);
            Assert.DoesNotContain("HISTORY-RESPONSE-MUST-NOT-EXPORT", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"RotationCount\"", json, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
