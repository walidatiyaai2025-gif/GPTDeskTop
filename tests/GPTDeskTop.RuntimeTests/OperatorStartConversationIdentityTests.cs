using GPTDeskTop.Data;
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
    public void OperatorStartUsesSafeSharedResolverAndServiceOwnedTargetCommit()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var service = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var uiStart = Slice(source, "private async Task StartMonitorAsync", "private ChromeTab? ResolveTab");
        var serviceStart = Slice(service, "public async Task StartMonitorAsync", "public async Task<bool> UpdateMonitorConfigurationAsync");

        Assert.Contains("SavedMonitorTabResolver.Resolve(monitor, _tabs).Tab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", uiStart, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.Url = tab.Url", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(monitor.Url, tab.Url)", serviceStart, StringComparison.Ordinal);
        Assert.Contains("await _chrome.GetTabsAsync()", serviceStart, StringComparison.Ordinal);
        Assert.Contains("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", serviceStart, StringComparison.Ordinal);
        Assert.Contains("MonitorLoopAsync(persistedMonitor, liveTab, cts.Token)", serviceStart, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
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
