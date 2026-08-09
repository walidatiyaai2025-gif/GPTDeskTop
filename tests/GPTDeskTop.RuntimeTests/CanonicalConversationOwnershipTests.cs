using GPTDeskTop.Data;
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
            var savedFresh = Assert.Single(await db.GetSavedMonitorsAsync(), m => m.Id == freshResult.MonitorId);
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
