from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    p.write_text(text.replace(old, new), encoding="utf-8")

# Data layer uses the shared logical identity semantics.
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    "using GPTDeskTop.Models;\n",
    "using GPTDeskTop.Models;\nusing GPTDeskTop.Services;\n")

# Runtime target update: atomic logical-source check instead of raw SQL URL equality.
old = r'''        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SavedMonitors
            SET TabId=$tabId, Title=$title, UpdatedAt=$updatedAt
            WHERE Id=$id AND Url=$expectedUrl COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$id", monitorId);
        command.Parameters.AddWithValue("$expectedUrl", expectedConversationUrl);
        command.Parameters.AddWithValue("$tabId", targetTabId);
        command.Parameters.AddWithValue("$title", targetTitle ?? string.Empty);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
'''
new = r'''        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            string? currentUrl = null;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                currentUrl = Convert.ToString(await load.ExecuteScalarAsync(cancellationToken));
            }
            if (string.IsNullOrWhiteSpace(currentUrl) || !ConversationIdentityMatches(currentUrl, expectedConversationUrl))
            {
                transaction.Rollback();
                return false;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, UpdatedAt=$updatedAt WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", monitorId);
            command.Parameters.AddWithValue("$tabId", targetTabId);
            command.Parameters.AddWithValue("$title", targetTitle ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            transaction.Commit();
            return updated;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
'''
replace_once("src/GPTDeskTop/Data/LocalDatabase.cs", old, new)

# Registration ownership lookup + canonical persistence.
old = r'''        ClampMonitorSettings(monitor);
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT Id FROM SavedMonitors WHERE Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                find.Parameters.AddWithValue("$url", monitor.Url);
                var existing = await find.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                {
                    var existingId = Convert.ToInt64(existing);
                    transaction.Commit();
                    monitor.Id = existingId;
                    return new MonitorRegistrationResult(existingId, false);
                }
            }

            await using var insert = connection.CreateCommand();
'''
new = r'''        ClampMonitorSettings(monitor);
        if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            monitor.Url = ChatGptConversationIdentity.Normalize(monitor.Url);
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            {
                var existingId = await FindLogicalConversationOwnerIdAsync(connection, transaction, monitor.Url, excludeMonitorId: null, cancellationToken);
                if (existingId.HasValue)
                {
                    transaction.Commit();
                    monitor.Id = existingId.Value;
                    return new MonitorRegistrationResult(existingId.Value, false);
                }
            }
            else
            {
                await using var find = connection.CreateCommand();
                find.Transaction = transaction;
                find.CommandText = "SELECT Id FROM SavedMonitors WHERE Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                find.Parameters.AddWithValue("$url", monitor.Url);
                var existing = await find.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                {
                    var existingId = Convert.ToInt64(existing);
                    transaction.Commit();
                    monitor.Id = existingId;
                    return new MonitorRegistrationResult(existingId, false);
                }
            }

            await using var insert = connection.CreateCommand();
'''
replace_once("src/GPTDeskTop/Data/LocalDatabase.cs", old, new)

# Rebind source/target logical comparisons and owner checks.
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''            if (!string.Equals(currentUrl, expectedCurrentUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("Saved monitor conversation identity changed before repair could be applied. Refresh and try again.");

            if (string.Equals(currentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve conversation ownership.");

            if (requireDuplicateSourceOwnership)
            {
                await using var sourceOwners = connection.CreateCommand();
                sourceOwners.Transaction = transaction;
                sourceOwners.CommandText = "SELECT COUNT(*) FROM SavedMonitors WHERE Url=$url COLLATE NOCASE;";
                sourceOwners.Parameters.AddWithValue("$url", currentUrl);
                var sourceOwnerCount = Convert.ToInt32(await sourceOwners.ExecuteScalarAsync(cancellationToken));
                if (sourceOwnerCount < 2)
                    throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");
            }

            await using (var targetOwner = connection.CreateCommand())
            {
                targetOwner.Transaction = transaction;
                targetOwner.CommandText = "SELECT Id FROM SavedMonitors WHERE Id<>$id AND Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                targetOwner.Parameters.AddWithValue("$id", monitorId);
                targetOwner.Parameters.AddWithValue("$url", targetUrl);
                var existing = await targetOwner.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                    throw new InvalidOperationException($"Monitor #{Convert.ToInt64(existing)} already owns the selected ChatGPT conversation.");
            }

            var now = DateTime.UtcNow.ToString("O");
''',
    '''            if (!ConversationIdentityMatches(currentUrl, expectedCurrentUrl))
                throw new InvalidOperationException("Saved monitor conversation identity changed before repair could be applied. Refresh and try again.");

            var canonicalTargetUrl = NormalizeStableConversationUrl(targetUrl);
            if (ChatGptConversationIdentity.IsSame(currentUrl, canonicalTargetUrl))
                throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve conversation ownership.");

            if (requireDuplicateSourceOwnership)
            {
                var sourceOwnerCount = await CountLogicalConversationOwnersAsync(connection, transaction, currentUrl, cancellationToken);
                if (sourceOwnerCount < 2)
                    throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");
            }

            var existingOwner = await FindLogicalConversationOwnerIdAsync(connection, transaction, canonicalTargetUrl, monitorId, cancellationToken);
            if (existingOwner.HasValue)
                throw new InvalidOperationException($"Monitor #{existingOwner.Value} already owns the selected ChatGPT conversation.");

            var now = DateTime.UtcNow.ToString("O");
''')
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''                update.Parameters.AddWithValue("$url", targetUrl);
''',
    '''                update.Parameters.AddWithValue("$url", canonicalTargetUrl);
''')
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''            return new MonitorConversationRebindDatabaseResult(monitorId, currentUrl, targetUrl);
''',
    '''            return new MonitorConversationRebindDatabaseResult(monitorId, currentUrl, canonicalTargetUrl);
''')

# Handoff logical source/target and canonical target persistence.
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''        if (string.Equals(expectedCurrentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Intentional handoff requires a different target conversation.");
''',
    '''        if (ChatGptConversationIdentity.IsSame(expectedCurrentUrl, targetUrl))
            throw new InvalidOperationException("Intentional handoff requires a different target conversation.");
''')
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''            if (!string.Equals(currentUrl, expectedCurrentUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("Saved monitor conversation identity changed before intentional handoff could be committed.");

            await using (var targetOwner = connection.CreateCommand())
            {
                targetOwner.Transaction = transaction;
                targetOwner.CommandText = "SELECT Id FROM SavedMonitors WHERE Id<>$id AND Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                targetOwner.Parameters.AddWithValue("$id", monitorId);
                targetOwner.Parameters.AddWithValue("$url", targetUrl);
                var existing = await targetOwner.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                    throw new InvalidOperationException($"Monitor #{Convert.ToInt64(existing)} already owns the intentional handoff target conversation.");
            }

            var nextRotationCount = incrementRotationCount ? checked(currentRotationCount + 1) : currentRotationCount;
''',
    '''            if (!ConversationIdentityMatches(currentUrl, expectedCurrentUrl))
                throw new InvalidOperationException("Saved monitor conversation identity changed before intentional handoff could be committed.");

            var canonicalTargetUrl = NormalizeStableConversationUrl(targetUrl);
            if (ChatGptConversationIdentity.IsSame(currentUrl, canonicalTargetUrl))
                throw new InvalidOperationException("Intentional handoff requires a different target conversation.");
            var existingOwner = await FindLogicalConversationOwnerIdAsync(connection, transaction, canonicalTargetUrl, monitorId, cancellationToken);
            if (existingOwner.HasValue)
                throw new InvalidOperationException($"Monitor #{existingOwner.Value} already owns the intentional handoff target conversation.");

            var nextRotationCount = incrementRotationCount ? checked(currentRotationCount + 1) : currentRotationCount;
''')
# This is the remaining handoff update url occurrence after rebind replacement above.
text_path = Path("src/GPTDeskTop/Data/LocalDatabase.cs")
text = text_path.read_text(encoding="utf-8")
needle = 'update.Parameters.AddWithValue("$url", targetUrl);'
if text.count(needle) != 1:
    raise RuntimeError(f"handoff target URL assignment expected once, found {text.count(needle)}")
text = text.replace(needle, 'update.Parameters.AddWithValue("$url", canonicalTargetUrl);')
text = text.replace('                targetUrl,\n                nextRotationCount,', '                canonicalTargetUrl,\n                nextRotationCount,', 1)
text_path.write_text(text, encoding="utf-8")

# Shared DB helpers before ClampMonitorSettings.
replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''    private static void ClampMonitorSettings(SavedMonitor monitor)
''',
    r'''    private static string NormalizeStableConversationUrl(string url)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(url))
            throw new InvalidOperationException("A stable ChatGPT conversation URL is required.");
        return ChatGptConversationIdentity.Normalize(url);
    }

    private static bool ConversationIdentityMatches(string currentUrl, string expectedUrl)
    {
        var currentStable = RuntimeHealthPresentation.IsChatGptConversationUrl(currentUrl);
        var expectedStable = RuntimeHealthPresentation.IsChatGptConversationUrl(expectedUrl);
        return currentStable && expectedStable
            ? ChatGptConversationIdentity.IsSame(currentUrl, expectedUrl)
            : string.Equals(currentUrl, expectedUrl, StringComparison.Ordinal);
    }

    private static async Task<long?> FindLogicalConversationOwnerIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        long? excludeMonitorId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = excludeMonitorId.HasValue
            ? "SELECT Id, Url FROM SavedMonitors WHERE Id<>$id ORDER BY Id;"
            : "SELECT Id, Url FROM SavedMonitors ORDER BY Id;";
        if (excludeMonitorId.HasValue)
            command.Parameters.AddWithValue("$id", excludeMonitorId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var url = reader.GetString(1);
            if (ChatGptConversationIdentity.IsSame(url, targetUrl))
                return id;
        }
        return null;
    }

    private static async Task<int> CountLogicalConversationOwnersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        CancellationToken cancellationToken)
    {
        var count = 0;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Url FROM SavedMonitors;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (ChatGptConversationIdentity.IsSame(reader.GetString(0), targetUrl))
                count++;
        }
        return count;
    }

    private static void ClampMonitorSettings(SavedMonitor monitor)
''')

# Analyzer uses canonical identity.
replace_once(
    "src/GPTDeskTop/Services/MonitorConversationOwnership.cs",
    '''                     .GroupBy(monitor => monitor.Url, StringComparer.OrdinalIgnoreCase)
''',
    '''                     .GroupBy(monitor => ChatGptConversationIdentity.Normalize(monitor.Url), StringComparer.Ordinal)
''')

# Service/UI prechecks use logical identity too.
replace_once(
    "src/GPTDeskTop/Services/MonitorIdentityRepairService.cs",
    '''            && string.Equals(saved.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase));
''',
    '''            && ChatGptConversationIdentity.IsSame(saved.Url, targetTab.Url));
''')
replace_once(
    "src/GPTDeskTop/Services/DuplicateOwnershipRepairService.cs",
    '''        if (string.Equals(monitor.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase))
''',
    '''        if (ChatGptConversationIdentity.IsSame(monitor.Url, targetTab.Url))
''')
replace_once(
    "src/GPTDeskTop/Services/DuplicateOwnershipRepairService.cs",
    '''            && string.Equals(saved.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase));
''',
    '''            && ChatGptConversationIdentity.IsSame(saved.Url, targetTab.Url));
''')
replace_once(
    "src/GPTDeskTop/UI/MainForm.cs",
    '''            var duplicate = _monitors.FirstOrDefault(m => string.Equals(m.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
''',
    '''            var duplicate = _monitors.FirstOrDefault(m => ChatGptConversationIdentity.IsSame(m.Url, tab.Url));
''')

# Tests.
Path("tests/GPTDeskTop.RuntimeTests/CanonicalConversationOwnershipTests.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CanonicalConversationOwnershipTests
{
    [Fact]
    public void AnalyzerTreatsTrailingSlashVariantsAsDuplicateOwnership()
    {
        var monitors = new[]
        {
            new SavedMonitor { Id = 1, Url = "https://chatgpt.com/c/canonical-dup" },
            new SavedMonitor { Id = 2, Url = "https://chatgpt.com/c/canonical-dup/" }
        };

        var ids = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);
        Assert.Equal(2, ids.Count);
        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
    }

    [Fact]
    public async Task RegistrationResolvesEquivalentLegacyOwnerAndPersistsCanonicalNewUrls()
    {
        var root = CreateRoot();
        try
        {
            var db = new LocalDatabase(Path.Combine(root, "test.db"));
            await db.InitializeAsync();
            var existing = new SavedMonitor { TabId = "old", Title = "Old", Url = "https://chatgpt.com/c/canon-register/" };
            var existingId = await db.SaveMonitorAsync(existing);

            var competing = new SavedMonitor { TabId = "new", Title = "New", Url = "https://chatgpt.com/c/canon-register" };
            var result = await db.RegisterMonitorIfConversationAvailableAsync(competing);
            Assert.False(result.Created);
            Assert.Equal(existingId, result.MonitorId);
            Assert.Single(await db.GetSavedMonitorsAsync());

            var fresh = new SavedMonitor { TabId = "fresh", Title = "Fresh", Url = "https://chatgpt.com/c/canon-fresh/" };
            var freshResult = await db.RegisterMonitorIfConversationAvailableAsync(fresh);
            Assert.True(freshResult.Created);
            var savedFresh = Assert.Single((await db.GetSavedMonitorsAsync()).Where(m => m.Id == freshResult.MonitorId));
            Assert.Equal("https://chatgpt.com/c/canon-fresh", savedFresh.Url);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task RepairRejectsLogicallyEquivalentOwnedTarget()
    {
        var root = CreateRoot();
        try
        {
            var db = new LocalDatabase(Path.Combine(root, "test.db"));
            await db.InitializeAsync();
            var invalidId = await db.SaveMonitorAsync(new SavedMonitor { TabId = "legacy", Title = "Legacy", Url = "https://chatgpt.com/" });
            await db.SaveMonitorAsync(new SavedMonitor { TabId = "owner", Title = "Owner", Url = "https://chatgpt.com/c/repair-owned/" });

            var service = new MonitorIdentityRepairService(db);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                invalidId,
                new ChromeTab { Id = "target", Title = "Target", Url = "https://chatgpt.com/c/repair-owned" }));
            Assert.Contains("already owns", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task HandoffRejectsEquivalentTargetOwnerAndAcceptsEquivalentExpectedSource()
    {
        var root = CreateRoot();
        try
        {
            var db = new LocalDatabase(Path.Combine(root, "test.db"));
            await db.InitializeAsync();
            var sourceId = await db.SaveMonitorAsync(new SavedMonitor { TabId = "source", Title = "Source", Url = "https://chatgpt.com/c/source-slash/" });
            await db.SaveMonitorAsync(new SavedMonitor { TabId = "owner", Title = "Owner", Url = "https://chatgpt.com/c/handoff-owned/" });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.CommitMonitorConversationHandoffAsync(
                sourceId,
                "https://chatgpt.com/c/source-slash",
                "target",
                "Target",
                "https://chatgpt.com/c/handoff-owned",
                true,
                true,
                "source",
                "AssistantMessageCount",
                "continue",
                "trigger",
                "Rotated",
                "Sent"));
            Assert.Contains("already owns", error.Message, StringComparison.OrdinalIgnoreCase);

            var success = await db.CommitMonitorConversationHandoffAsync(
                sourceId,
                "https://chatgpt.com/c/source-slash",
                "fresh-target",
                "Fresh",
                "https://chatgpt.com/c/handoff-fresh/",
                true,
                true,
                "source",
                "AssistantMessageCount",
                "continue",
                "trigger",
                "Rotated",
                "Sent");
            Assert.Equal("https://chatgpt.com/c/handoff-fresh", success.NewUrl);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task RuntimeTargetGuardAcceptsCanonicalEquivalentExpectedSourceButRejectsDifferentConversation()
    {
        var root = CreateRoot();
        try
        {
            var db = new LocalDatabase(Path.Combine(root, "test.db"));
            await db.InitializeAsync();
            var id = await db.SaveMonitorAsync(new SavedMonitor { TabId = "old", Title = "Old", Url = "https://chatgpt.com/c/runtime-source/" });
            Assert.True(await db.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(id, "https://chatgpt.com/c/runtime-source", "new", "New"));
            Assert.False(await db.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(id, "https://chatgpt.com/c/other", "bad", "Bad"));
            var saved = Assert.Single(await db.GetSavedMonitorsAsync(), m => m.Id == id);
            Assert.Equal("new", saved.TabId);
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { Directory.Delete(root, true); } catch { }
    }
}
''', encoding="utf-8")

# Status update.
replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''- Config-Only Existing Monitor Settings Save — PR #72: operator edits to an existing monitor now update only editable configuration columns. Runtime identity and state (`TabId`, `Title`, `Url`, `RotationCount`, Monitor ID/history identity) remain database-owned, so a stale settings dialog cannot roll back a concurrent repair, recovery or intentional handoff. The UI reloads the monitor after save and handles deletion while the dialog was open without recreating the row.\n''',
    '''- Config-Only Existing Monitor Settings Save is merged on `main` (`04351ee4e3ff2b2690ec1f3307040c960c0f7a04`): operator edits to an existing monitor now update only editable configuration columns. Runtime identity and state (`TabId`, `Title`, `Url`, `RotationCount`, Monitor ID/history identity) remain database-owned, so a stale settings dialog cannot roll back a concurrent repair, recovery or intentional handoff. The UI reloads the monitor after save and handles deletion while the dialog was open without recreating the row.\n''')
replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After config-only existing-monitor settings persistence, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #73 is the current tracked post-1.8 task: unify duplicate ownership detection and SQLite registration/repair/handoff guards on canonical stable-conversation identity so logically equivalent URL spellings cannot bypass ownership safety. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #73 patch applied.")
