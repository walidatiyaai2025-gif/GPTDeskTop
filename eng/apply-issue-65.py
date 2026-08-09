from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


Path("src/GPTDeskTop/Services/ChatGptConversationIdentity.cs").write_text(r'''namespace GPTDeskTop.Services;

/// <summary>
/// Compares stable ChatGPT conversation URLs as logical monitor identities.
/// Chrome DevTools target IDs are runtime locators only and must never be used
/// to move a monitor to a different conversation.
/// </summary>
public static class ChatGptConversationIdentity
{
    public static bool IsSame(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(left)
            || !RuntimeHealthPresentation.IsChatGptConversationUrl(right))
            return false;

        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed.TrimEnd('/');

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;
    }
}
''', encoding="utf-8")

replace_once(
    "src/GPTDeskTop/Services/DevelopmentTaskEngine/SavedMonitorTabResolver.cs",
    '''        if (!string.IsNullOrWhiteSpace(monitor.TabId))\n        {\n            var exact = tabs.FirstOrDefault(tab =>\n                string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal));\n            if (exact is not null\n                && RuntimeHealthPresentation.IsChatGptConversationUrl(exact.Url))\n                return SavedMonitorTabResolution.CreateFound(exact, "PersistedTabId");\n        }\n\n        var sameConversation = tabs.FirstOrDefault(tab =>\n            RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)\n            && string.Equals(NormalizeUrl(tab.Url), NormalizeUrl(monitor.Url), StringComparison.Ordinal));\n''',
    '''        if (!string.IsNullOrWhiteSpace(monitor.TabId))\n        {\n            var exact = tabs.FirstOrDefault(tab =>\n                string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal));\n            if (exact is not null\n                && ChatGptConversationIdentity.IsSame(exact.Url, monitor.Url))\n                return SavedMonitorTabResolution.CreateFound(exact, "PersistedTabId");\n        }\n\n        var sameConversation = tabs.FirstOrDefault(tab =>\n            ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));\n''')

replace_once(
    "src/GPTDeskTop/Services/DevelopmentTaskEngine/SavedMonitorTabResolver.cs",
    '''\n    private static string NormalizeUrl(string value)\n    {\n        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))\n            return value.Trim().TrimEnd('/');\n        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;\n    }\n''',
    '''\n''')

replace_once(
    "src/GPTDeskTop/Data/LocalDatabase.cs",
    '''    public async Task<MonitorRegistrationResult> RegisterMonitorIfConversationAvailableAsync(\n''',
    r'''    public async Task<bool> UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
        long monitorId,
        string expectedConversationUrl,
        string targetTabId,
        string targetTitle,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(expectedConversationUrl))
            throw new InvalidOperationException("The saved monitor conversation identity is required.");
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The resolved Chrome target ID is required.");

        var now = DateTime.UtcNow;
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
    }

    public async Task<MonitorRegistrationResult> RegisterMonitorIfConversationAvailableAsync(
''')

replace_once(
    "src/GPTDeskTop/UI/MainForm.cs",
    '''using GPTDeskTop.Services;\n''',
    '''using GPTDeskTop.Services;\nusing GPTDeskTop.Services.DevelopmentTaskEngine;\n''')

replace_once(
    "src/GPTDeskTop/UI/MainForm.cs",
    '''        monitor.TabId = tab.Id;\n        monitor.Title = tab.Title;\n        monitor.Url = tab.Url;\n        await _database.SaveMonitorAsync(monitor);\n        await _monitor.StartMonitorAsync(monitor, tab);\n        await RefreshMonitorsAsync();\n    }\n\n    private ChromeTab? ResolveTab(SavedMonitor monitor)\n        => _tabs.FirstOrDefault(t => t.Id == monitor.TabId)\n           ?? _tabs.FirstOrDefault(t => string.Equals(t.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));\n''',
    '''        var expectedConversationUrl = monitor.Url;\n        var targetUpdated = await _database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(\n            monitor.Id,\n            expectedConversationUrl,\n            tab.Id,\n            tab.Title);\n        if (!targetUpdated)\n        {\n            AppendActivity($"Monitor #{monitor.Id}: saved conversation changed before Start could update the Chrome target. Refreshing monitor state.");\n            await RefreshMonitorsAsync();\n            return;\n        }\n\n        monitor.TabId = tab.Id;\n        monitor.Title = tab.Title;\n        await _monitor.StartMonitorAsync(monitor, tab);\n        await RefreshMonitorsAsync();\n    }\n\n    private ChromeTab? ResolveTab(SavedMonitor monitor)\n        => SavedMonitorTabResolver.Resolve(monitor, _tabs).Tab;\n''')

replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");\n\n        var savedMonitors = await _database.GetSavedMonitorsAsync();\n''',
    '''        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");\n        if (!ChatGptConversationIdentity.IsSame(monitor.Url, tab.Url))\n            throw new InvalidOperationException("The selected Chrome target no longer represents the saved ChatGPT conversation identity.");\n\n        var savedMonitors = await _database.GetSavedMonitorsAsync();\n''')

replace_once(
    "tests/GPTDeskTop.RuntimeTests/SavedMonitorTabResolverTests.cs",
    '''    public void ExactPersistedTabIdWins()\n    {\n        var monitor = new SavedMonitor { TabId = "tab-2", Url = "https://chatgpt.com/c/conversation-2" };\n        var tabs = new[]\n        {\n            new ChromeTab { Id = "tab-1", Url = "https://chatgpt.com/c/conversation-2" },\n            new ChromeTab { Id = "tab-2", Url = "https://chatgpt.com/c/other" }\n        };\n\n        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);\n\n        Assert.True(result.Found);\n        Assert.Equal("tab-2", result.Tab!.Id);\n        Assert.Equal("PersistedTabId", result.MatchType);\n    }\n''',
    '''    public void StalePersistedTabIdForDifferentConversationFallsBackToSavedConversationUrl()\n    {\n        var monitor = new SavedMonitor { TabId = "tab-2", Url = "https://chatgpt.com/c/conversation-2" };\n        var tabs = new[]\n        {\n            new ChromeTab { Id = "tab-1", Url = "https://chatgpt.com/c/conversation-2" },\n            new ChromeTab { Id = "tab-2", Url = "https://chatgpt.com/c/other" }\n        };\n\n        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);\n\n        Assert.True(result.Found);\n        Assert.Equal("tab-1", result.Tab!.Id);\n        Assert.Equal("PersistedConversationUrl", result.MatchType);\n    }\n\n    [Fact]\n    public void ExactPersistedTabIdWinsOnlyForSameConversation()\n    {\n        var monitor = new SavedMonitor { TabId = "tab-2", Url = "https://chatgpt.com/c/conversation-2/" };\n        var tabs = new[]\n        {\n            new ChromeTab { Id = "tab-1", Url = "https://chatgpt.com/c/conversation-2" },\n            new ChromeTab { Id = "tab-2", Url = "https://chatgpt.com/c/conversation-2" }\n        };\n\n        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);\n\n        Assert.True(result.Found);\n        Assert.Equal("tab-2", result.Tab!.Id);\n        Assert.Equal("PersistedTabId", result.MatchType);\n    }\n\n    [Fact]\n    public void ReusedPersistedTabIdWithoutSavedConversationOpenIsMissing()\n    {\n        var monitor = new SavedMonitor { TabId = "reused", Url = "https://chatgpt.com/c/original" };\n        var tabs = new[]\n        {\n            new ChromeTab { Id = "reused", Url = "https://chatgpt.com/c/different" }\n        };\n\n        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);\n\n        Assert.False(result.Found);\n        Assert.Null(result.Tab);\n        Assert.Equal("None", result.MatchType);\n    }\n''')

Path("tests/GPTDeskTop.RuntimeTests/OperatorStartConversationIdentityTests.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class OperatorStartConversationIdentityTests
{
    [Fact]
    public void ConversationIdentityNormalizesTrailingSlashButRejectsDifferentConversation()
    {
        Assert.True(ChatGptConversationIdentity.IsSame(
            "https://chatgpt.com/c/conversation-1/",
            "https://chatgpt.com/c/conversation-1"));
        Assert.False(ChatGptConversationIdentity.IsSame(
            "https://chatgpt.com/c/conversation-1",
            "https://chatgpt.com/c/conversation-2"));
        Assert.False(ChatGptConversationIdentity.IsSame(
            "https://chatgpt.com/",
            "https://chatgpt.com/c/conversation-1"));
    }

    [Fact]
    public async Task RuntimeTargetUpdateNeverChangesConversationUrlOrSettings()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "old-target",
                Title = "Original title",
                Url = "https://chatgpt.com/c/original",
                AutoReply = "keep this",
                ReplyDelaySeconds = 17,
                TimerSeconds = 7,
                Enabled = false,
                ConversationRotationEnabled = true,
                RotationCount = 4
            };
            var monitorId = await database.SaveMonitorAsync(monitor);

            var updated = await database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
                monitorId,
                "https://chatgpt.com/c/original",
                "new-target",
                "Updated title");

            Assert.True(updated);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("new-target", saved.TabId);
            Assert.Equal("Updated title", saved.Title);
            Assert.Equal("https://chatgpt.com/c/original", saved.Url);
            Assert.Equal("keep this", saved.AutoReply);
            Assert.Equal(17, saved.ReplyDelaySeconds);
            Assert.Equal(7, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.True(saved.ConversationRotationEnabled);
            Assert.Equal(4, saved.RotationCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RuntimeTargetUpdateRejectsConcurrentConversationChangeWithoutOverwritingRepair()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "old-target",
                Title = "Original",
                Url = "https://chatgpt.com/c/original"
            };
            var monitorId = await database.SaveMonitorAsync(monitor);
            monitor.Url = "https://chatgpt.com/c/repaired";
            monitor.TabId = "repair-target";
            monitor.Title = "Repaired";
            await database.SaveMonitorAsync(monitor);

            var updated = await database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
                monitorId,
                "https://chatgpt.com/c/original",
                "stale-target",
                "Stale title");

            Assert.False(updated);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitorId);
            Assert.Equal("https://chatgpt.com/c/repaired", saved.Url);
            Assert.Equal("repair-target", saved.TabId);
            Assert.Equal("Repaired", saved.Title);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void OperatorStartUsesSafeSharedResolverAndNeverAssignsTabUrlToMonitor()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var service = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.Contains("SavedMonitorTabResolver.Resolve(monitor, _tabs).Tab", source, StringComparison.Ordinal);
        Assert.Contains("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.Url = tab.Url", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(monitor.Url, tab.Url)", service, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
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
''', encoding="utf-8")

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''- Transactional Conversation Rebind Ownership Guard is implemented in PR #64: invalid-identity and duplicate-owner repair now converge on `RebindMonitorConversationIfAvailableAsync`, which acquires a non-deferred SQLite writer transaction, revalidates the source snapshot, rechecks duplicate-source ownership when required, verifies the target remains unowned with `COLLATE NOCASE`, updates only the runtime conversation binding fields and writes the remediation diagnostic in the same transaction. This closes the repair-vs-registration and repair-vs-repair TOCTOU path without changing Monitor ID, history identity, operator configuration, enabled state, rotation state or crash-recovery state.\n''',
    '''- Transactional Conversation Rebind Ownership Guard is merged on `main` (`eaabca8c8563b39aa0763546c33ca07b3904f18f`): invalid-identity and duplicate-owner repair now converge on `RebindMonitorConversationIfAvailableAsync`, which acquires a non-deferred SQLite writer transaction, revalidates the source snapshot, rechecks duplicate-source ownership when required, verifies the target remains unowned with `COLLATE NOCASE`, updates only the runtime conversation binding fields and writes the remediation diagnostic in the same transaction. This closes the repair-vs-registration and repair-vs-repair TOCTOU path without changing Monitor ID, history identity, operator configuration, enabled state, rotation state or crash-recovery state.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After the transactional conversation-rebind ownership guard, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #65 is the current tracked post-1.8 task: reject stale/reused Chrome target IDs unless the live target still represents the saved stable ChatGPT conversation, use exact saved-conversation URL fallback for recreated targets, and ensure ordinary Start can update runtime target metadata without ever changing the persisted conversation identity. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #65 patch applied.")
