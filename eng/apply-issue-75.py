from pathlib import Path

# ConfigurationBackupImportService: canonicalize validated monitor identities and reject logical duplicates.
service_path = Path('src/GPTDeskTop/Services/ConfigurationBackupImportService.cs')
service = service_path.read_text(encoding='utf-8')
start = service.index('    private static IReadOnlyList<SavedMonitor> ValidateMonitors(')
end = service.rfind('\n}')
new_validate = r'''    private static IReadOnlyList<SavedMonitor> ValidateMonitors(
        IReadOnlyList<ConfigurationBackupMonitor> monitors)
    {
        var result = new List<SavedMonitor>(monitors.Count);
        var conversationIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            if (monitor is null)
                throw new InvalidDataException("The configuration backup contains a null monitor entry.");

            var title = (monitor.Title ?? string.Empty).Trim();
            var url = (monitor.Url ?? string.Empty).Trim();
            var autoReply = (monitor.AutoReply ?? string.Empty).Trim();
            var startMessage = (monitor.NewChatStartMessage ?? string.Empty).Trim();
            var preferredModel = string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel.Trim();
            var fallbackModel = string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel.Trim();

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidDataException("A monitor title cannot be empty.");
            if (title.Length > 1000)
                throw new InvalidDataException($"Monitor title '{title[..Math.Min(title.Length, 80)]}' exceeds 1000 characters.");
            if (string.IsNullOrWhiteSpace(url) || url.Length > 4096)
                throw new InvalidDataException($"Monitor '{title}' has an invalid conversation URL length.");
            if (!RuntimeHealthPresentation.IsChatGptConversationUrl(url))
                throw new InvalidDataException($"Monitor '{title}' must use an absolute HTTPS ChatGPT conversation URL with a stable /c/{{conversation-id}} identity.");

            var canonicalUrl = ChatGptConversationIdentity.Normalize(url);
            if (!conversationIdentities.Add(canonicalUrl))
                throw new InvalidDataException($"Conversation identity '{canonicalUrl}' appears more than once in the configuration backup.");
            if (string.IsNullOrWhiteSpace(autoReply) || autoReply.Length > MaxMessageChars)
                throw new InvalidDataException($"Monitor '{title}' has an empty or oversized auto reply.");
            if (monitor.ConversationRotationEnabled && string.IsNullOrWhiteSpace(startMessage))
                throw new InvalidDataException($"Monitor '{title}' requires a new-chat start message while rotation is enabled.");
            if (startMessage.Length > MaxMessageChars)
                throw new InvalidDataException($"Monitor '{title}' has an oversized new-chat start message.");
            if (monitor.ReplyDelaySeconds < 0 || monitor.ReplyDelaySeconds > 300)
                throw new InvalidDataException($"Monitor '{title}' reply delay must be between 0 and 300 seconds.");
            if (monitor.TimerSeconds < 1 || monitor.TimerSeconds > 60)
                throw new InvalidDataException($"Monitor '{title}' polling timer must be between 1 and 60 seconds.");
            if (monitor.NewChatDelaySeconds < 0 || monitor.NewChatDelaySeconds > 600)
                throw new InvalidDataException($"Monitor '{title}' new-chat delay must be between 0 and 600 seconds.");
            if (monitor.RotationCooldownSeconds < 0 || monitor.RotationCooldownSeconds > 3600)
                throw new InvalidDataException($"Monitor '{title}' rotation cooldown must be between 0 and 3600 seconds.");
            if (monitor.MaxConversationRotations < 0 || monitor.MaxConversationRotations > 1000)
                throw new InvalidDataException($"Monitor '{title}' maximum rotations must be between 0 and 1000.");
            if (preferredModel.Length > 200 || fallbackModel.Length > 200)
                throw new InvalidDataException($"Monitor '{title}' contains an oversized model label.");

            result.Add(new SavedMonitor
            {
                Id = 0,
                TabId = string.Empty,
                Title = title,
                Url = canonicalUrl,
                AutoReply = autoReply,
                ReplyDelaySeconds = monitor.ReplyDelaySeconds,
                TimerSeconds = monitor.TimerSeconds,
                Enabled = monitor.Enabled,
                ConversationRotationEnabled = monitor.ConversationRotationEnabled,
                NewChatStartMessage = startMessage,
                NewChatDelaySeconds = monitor.NewChatDelaySeconds,
                RotationCooldownSeconds = monitor.RotationCooldownSeconds,
                MaxConversationRotations = monitor.MaxConversationRotations,
                RotationCount = 0,
                ModelRoutingEnabled = monitor.ModelRoutingEnabled,
                PreferredModel = preferredModel,
                FallbackModel = fallbackModel
            });
        }

        return result;
    }
'''
service = service[:start] + new_validate + service[end:]
service_path.write_text(service, encoding='utf-8')

# LocalDatabase: defensive logical-payload validation before mutation, immediate writer transaction,
# canonical local matching, and canonical inserts while preserving existing local URL/runtime identity.
db_path = Path('src/GPTDeskTop/Data/LocalDatabase.cs')
db = db_path.read_text(encoding='utf-8')
start = db.index('    public async Task<ConfigurationImportDatabaseResult> ApplyConfigurationImportAsync(')
end = db.index('    public async Task DeleteMonitorAsync', start)
new_import = r'''    public async Task<ConfigurationImportDatabaseResult> ApplyConfigurationImportAsync(
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyList<SavedMonitor> monitors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitors);

        // Defend the persistence boundary even when a caller bypasses CreatePlan.
        // This validation intentionally happens before opening a transaction or writing settings.
        var canonicalImportUrls = new string[monitors.Count];
        var importedConversationIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index] ?? throw new InvalidOperationException("The configuration import contains a null monitor entry.");
            var canonicalUrl = NormalizeStableConversationUrl(monitor.Url ?? string.Empty);
            if (!importedConversationIdentities.Add(canonicalUrl))
                throw new InvalidOperationException($"Configuration import contains the same logical ChatGPT conversation more than once: '{canonicalUrl}'.");
            canonicalImportUrls[index] = canonicalUrl;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        var settingsApplied = 0;
        var monitorsUpdated = 0;
        var monitorsInserted = 0;
        var now = DateTime.UtcNow.ToString("O");

        try
        {
            foreach (var pair in settings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var settingCommand = connection.CreateCommand();
                settingCommand.Transaction = transaction;
                settingCommand.CommandText = "INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
                settingCommand.Parameters.AddWithValue("$key", pair.Key);
                settingCommand.Parameters.AddWithValue("$value", pair.Value ?? string.Empty);
                await settingCommand.ExecuteNonQueryAsync(cancellationToken);
                settingsApplied++;
            }

            for (var index = 0; index < monitors.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monitor = monitors[index];
                var canonicalUrl = canonicalImportUrls[index];
                var matchingIds = await FindLogicalConversationOwnerIdsAsync(
                    connection,
                    transaction,
                    canonicalUrl,
                    maxCount: 2,
                    cancellationToken);

                if (matchingIds.Count > 1)
                    throw new InvalidOperationException($"Cannot import monitor '{monitor.Url}' because more than one local monitor owns that logical conversation identity.");

                await using var monitorCommand = connection.CreateCommand();
                monitorCommand.Transaction = transaction;

                if (matchingIds.Count == 1)
                {
                    monitorCommand.CommandText = """
                        UPDATE SavedMonitors SET
                            Title=$title, AutoReply=$autoReply, ReplyDelaySeconds=$replyDelay, TimerSeconds=$timer, Enabled=$enabled,
                            ConversationRotationEnabled=$rotationEnabled, NewChatStartMessage=$message, NewChatDelaySeconds=$newChatDelay,
                            RotationCooldownSeconds=$cooldown, MaxConversationRotations=$maxRotations,
                            ModelRoutingEnabled=$modelRouting, PreferredModel=$preferredModel, FallbackModel=$fallbackModel, UpdatedAt=$updatedAt
                        WHERE Id=$id;
                        """;
                    monitorCommand.Parameters.AddWithValue("$id", matchingIds[0]);
                    monitorsUpdated++;
                }
                else
                {
                    monitorCommand.CommandText = """
                        INSERT INTO SavedMonitors(
                            TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                            NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                            ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt)
                        VALUES('', $title,$url,$autoReply,$replyDelay,$timer,$enabled,$rotationEnabled,$message,$newChatDelay,$cooldown,$maxRotations,0,$modelRouting,$preferredModel,$fallbackModel,$createdAt,$updatedAt);
                        """;
                    monitorCommand.Parameters.AddWithValue("$url", canonicalUrl);
                    monitorCommand.Parameters.AddWithValue("$createdAt", now);
                    monitorsInserted++;
                }

                monitorCommand.Parameters.AddWithValue("$title", monitor.Title ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$replyDelay", Math.Clamp(monitor.ReplyDelaySeconds, 0, 300));
                monitorCommand.Parameters.AddWithValue("$timer", Math.Clamp(monitor.TimerSeconds, 1, 60));
                monitorCommand.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$message", monitor.NewChatStartMessage ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$newChatDelay", Math.Clamp(monitor.NewChatDelaySeconds, 0, 600));
                monitorCommand.Parameters.AddWithValue("$cooldown", Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600));
                monitorCommand.Parameters.AddWithValue("$maxRotations", Math.Clamp(monitor.MaxConversationRotations, 0, 1000));
                monitorCommand.Parameters.AddWithValue("$modelRouting", monitor.ModelRoutingEnabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$preferredModel", string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel);
                monitorCommand.Parameters.AddWithValue("$fallbackModel", string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel);
                monitorCommand.Parameters.AddWithValue("$updatedAt", now);
                await monitorCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new ConfigurationImportDatabaseResult(settingsApplied, monitorsUpdated, monitorsInserted);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task<List<long>> FindLogicalConversationOwnerIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>(Math.Max(1, Math.Min(maxCount, 8)));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Url FROM SavedMonitors ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!ChatGptConversationIdentity.IsSame(reader.GetString(1), targetUrl))
                continue;
            ids.Add(reader.GetInt64(0));
            if (ids.Count >= maxCount)
                break;
        }
        return ids;
    }

'''
db = db[:start] + new_import + db[end:]
db_path.write_text(db, encoding='utf-8')

# Focused real-SQLite regression coverage.
test_path = Path('tests/GPTDeskTop.RuntimeTests/CanonicalConfigurationImportOwnershipTests.cs')
test_path.write_text(r'''using GPTDeskTop.Data;
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
''', encoding='utf-8')

# Development status: reconcile #74 merge and track #75 implementation.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Canonical Stable-Conversation Ownership is implemented in PR #74:',
    '- Canonical Stable-Conversation Ownership is merged on `main` (`0e148ffd353010483289600083061bbdf903d8ca`):',
    1)
active = '- Canonical Configuration Import Ownership is implemented for Issue #75: configuration backup import now validates logical conversation identities before mutation, merges against canonical local ownership while preserving an existing local runtime binding and stored URL spelling, rolls back on ambiguous legacy logical ownership, and stores canonical URLs only for genuinely new imported monitors.'
if active not in status:
    anchor = '- Canonical Stable-Conversation Ownership is merged on `main` (`0e148ffd353010483289600083061bbdf903d8ca`):'
    pos = status.index(anchor)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After canonical stable-conversation ownership, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #75 is the current tracked post-1.8 task: make configuration backup import use canonical stable-conversation ownership so equivalent URL spellings cannot create or hide duplicate monitor ownership. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #75 implementation patch applied.')
