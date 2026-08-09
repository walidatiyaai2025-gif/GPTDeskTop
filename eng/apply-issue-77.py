from pathlib import Path

# ConfigurationBackupService: refuse snapshots that current import cannot restore,
# and canonicalize valid stable conversation URLs in portable output.
service_path = Path('src/GPTDeskTop/Services/ConfigurationBackupService.cs')
service = service_path.read_text(encoding='utf-8')
old = '''        settings ??= new Dictionary<string, string?>();
        monitors ??= Array.Empty<SavedMonitor>();

        var projectedSettings = AllowedSettingKeys
            .Where(key => settings.TryGetValue(key, out var value) && value is not null)
            .Select(key => new ConfigurationBackupSetting(key, settings[key] ?? string.Empty))
            .ToArray();

        var projectedMonitors = monitors
            .Select(CreateMonitorBackup)
            .ToArray();
'''
new = '''        settings ??= new Dictionary<string, string?>();
        monitors ??= Array.Empty<SavedMonitor>();

        var monitorSnapshot = monitors.ToArray();
        var invalidIdentity = monitorSnapshot.FirstOrDefault(monitor =>
            !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url));
        if (invalidIdentity is not null)
        {
            throw new InvalidOperationException(
                $"Configuration backup cannot be created because monitor #{invalidIdentity.Id} does not have a stable ChatGPT conversation identity. Use Runtime Health Repair before exporting a portable backup.");
        }

        var duplicateMonitorIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitorSnapshot);
        if (duplicateMonitorIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration backup cannot be created while {duplicateMonitorIds.Count} monitors are blocked by duplicate ChatGPT conversation ownership. Use Runtime Health Repair before exporting a portable backup.");
        }

        var projectedSettings = AllowedSettingKeys
            .Where(key => settings.TryGetValue(key, out var value) && value is not null)
            .Select(key => new ConfigurationBackupSetting(key, settings[key] ?? string.Empty))
            .ToArray();

        var projectedMonitors = monitorSnapshot
            .Select(CreateMonitorBackup)
            .ToArray();
'''
if service.count(old) != 1:
    raise RuntimeError(f'ConfigurationBackupService projection anchor count={service.count(old)}')
service = service.replace(old, new, 1)
old_url = '''            monitor.Title ?? string.Empty,
            monitor.Url ?? string.Empty,
            monitor.AutoReply ?? string.Empty,
'''
new_url = '''            monitor.Title ?? string.Empty,
            ChatGptConversationIdentity.Normalize(monitor.Url ?? string.Empty),
            monitor.AutoReply ?? string.Empty,
'''
if service.count(old_url) != 1:
    raise RuntimeError(f'ConfigurationBackupService URL projection anchor count={service.count(old_url)}')
service = service.replace(old_url, new_url, 1)
service_path.write_text(service, encoding='utf-8')

# Settings copy: import now merges by canonical/logical conversation identity rather than raw exact URL.
settings_path = Path('src/GPTDeskTop/UI/SettingsForm.cs')
settings = settings_path.read_text(encoding='utf-8')
old_copy = '"Exact conversation-URL matches update only operator configuration while preserving the local monitor ID, runtime Tab ID, rotation counter and history. " +'
new_copy = '"Canonical conversation-identity matches update only operator configuration while preserving the local monitor ID, runtime Tab ID, stored URL spelling, rotation counter and history. " +'
if settings.count(old_copy) != 1:
    raise RuntimeError(f'SettingsForm import copy anchor count={settings.count(old_copy)}')
settings = settings.replace(old_copy, new_copy, 1)
settings_path.write_text(settings, encoding='utf-8')

# Unit/real-SQLite coverage for round-trip export safety.
test_path = Path('tests/GPTDeskTop.RuntimeTests/ConfigurationBackupRoundTripSafetyTests.cs')
test_path.write_text(r'''using GPTDeskTop.Data;
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
''', encoding='utf-8')

# Source-contract coverage: export must enforce round-trip identity and UI must describe logical matching.
ui_test_path = Path('tests/GPTDeskTop.RuntimeTests/ConfigurationBackupUiRegressionTests.cs')
ui = ui_test_path.read_text(encoding='utf-8')
needle = '        Assert.Contains("GetSavedMonitorsAsync", source, StringComparison.Ordinal);\n'
replacement = needle + '''        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl", source, StringComparison.Ordinal);\n        Assert.Contains("MonitorConversationOwnership.FindDuplicateMonitorIds", source, StringComparison.Ordinal);\n        Assert.Contains("ChatGptConversationIdentity.Normalize", source, StringComparison.Ordinal);\n'''
if ui.count(needle) != 1:
    raise RuntimeError(f'Backup UI regression source anchor count={ui.count(needle)}')
ui = ui.replace(needle, replacement, 1)
insert_before = '''    [Fact]\n    public void SchemaDocumentsSensitiveAndExcludedState()\n'''
new_test = '''    [Fact]\n    public void ImportConfirmationDescribesCanonicalConversationIdentityMatching()\n    {\n        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");\n\n        Assert.Contains("Canonical conversation-identity matches update only operator configuration", source, StringComparison.Ordinal);\n        Assert.DoesNotContain("Exact conversation-URL matches update only operator configuration", source, StringComparison.Ordinal);\n        Assert.Contains("stored URL spelling", source, StringComparison.Ordinal);\n    }\n\n'''
if ui.count(insert_before) != 1:
    raise RuntimeError(f'Backup UI regression insertion anchor count={ui.count(insert_before)}')
ui = ui.replace(insert_before, new_test + insert_before, 1)
ui_test_path.write_text(ui, encoding='utf-8')

# Configuration backup docs: document round-trip export blocker semantics.
docs_path = Path('docs/CONFIGURATION_BACKUP.md')
docs = docs_path.read_text(encoding='utf-8')
anchor = '## Import behavior\n'
block = '''## Export round-trip safety\n\nA configuration backup is created only when every saved monitor has a stable ChatGPT `/c/...` conversation identity and no two saved monitors own the same logical conversation. Legacy invalid identities or canonical-equivalent duplicate owners must be repaired through **Runtime Health Repair** before export. GPTDeskTop never silently drops or merges monitors during export. Stable conversation URLs written to the portable backup are canonicalized. If validation fails, an existing destination backup is left unchanged and temporary export files are removed.\n\n'''
if block not in docs:
    if anchor not in docs:
        raise RuntimeError('CONFIGURATION_BACKUP import behavior anchor not found')
    docs = docs.replace(anchor, block + anchor, 1)
docs = docs.replace('Exact conversation URL matches', 'Canonical conversation identity matches')
docs_path.write_text(docs, encoding='utf-8')

# Development status: reconcile #76 merge and track #77.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Canonical Configuration Import Ownership is implemented in PR #76:',
    '- Canonical Configuration Import Ownership is merged on `main` (`acdbd71183d12a52b855f7cd2da57f67b601c93c`):',
    1)
active = '- Configuration Backup Round-Trip Safety is implemented for Issue #77: portable export now refuses legacy invalid monitor identities and canonical-equivalent duplicate ownership instead of producing a backup the current importer cannot restore, canonicalizes every valid exported conversation URL, preserves atomic destination replacement semantics on validation failure, and updates import confirmation copy to describe canonical conversation-identity matching.'
if active not in status:
    anchor_status = '- Canonical Configuration Import Ownership is merged on `main` (`acdbd71183d12a52b855f7cd2da57f67b601c93c`):'
    pos = status.index(anchor_status)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After canonical configuration-import ownership, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #77 is the current tracked post-1.8 task: make configuration backup export round-trippable by refusing invalid or duplicate stable-conversation ownership before file creation, canonicalizing valid exported conversation URLs, and keeping operator copy aligned with canonical import matching. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected post-#75 Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #77 round-trip export safety patch applied.')
