using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class OperatorStartConversationIdentityTests
{
    [Fact]
    public void ResolverRejectsStaleTabIdWhenItPointsToDifferentConversation()
    {
        var monitor = new SavedMonitor
        {
            Id = 17,
            TabId = "reused-target-id",
            Title = "Saved conversation",
            Url = "https://chatgpt.com/c/saved-conversation"
        };
        var tabs = new[]
        {
            Tab("reused-target-id", "Different conversation", "https://chatgpt.com/c/different-conversation")
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.False(result.Found);
        Assert.Null(result.Tab);
        Assert.Equal("None", result.MatchType);
        Assert.Contains("not currently open", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolverRebindsExactConversationWhenTargetIdChanged()
    {
        var monitor = new SavedMonitor
        {
            Id = 18,
            TabId = "old-target-id",
            Title = "Saved conversation",
            Url = "https://chatgpt.com/c/saved-conversation"
        };
        var tabs = new[]
        {
            Tab("new-target-id", "Saved conversation reloaded", "https://chatgpt.com/c/saved-conversation")
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.True(result.Found);
        Assert.NotNull(result.Tab);
        Assert.Equal("new-target-id", result.Tab!.Id);
        Assert.Equal("PersistedConversationUrl", result.MatchType);
    }

    [Fact]
    public async Task ConditionalRuntimeTargetUpdateCannotOverwriteConversationHandoff()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "operator-start.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "original-target",
                Title = "Original",
                Url = "https://chatgpt.com/c/original",
                AutoReply = "كمل",
                Enabled = true
            };
            var monitorId = await database.SaveMonitorAsync(monitor);

            var handoff = await database.CommitMonitorConversationHandoffAsync(
                monitorId,
                "https://chatgpt.com/c/original",
                "repair-target",
                "Repaired",
                "https://chatgpt.com/c/repaired",
                incrementRotationCount: false,
                recordRotation: false,
                oldTabId: "original-target",
                rotationTrigger: "Test",
                startMessage: "كمل",
                triggerResponse: "handoff",
                successStatus: "TestHandoff",
                outboundStatus: "TestOutbound");
            Assert.Equal("https://chatgpt.com/c/repaired", handoff.NewUrl);

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
        Assert.Contains("ChatGptConversationIdentity.IsSame(persistedMonitor.Url", serviceStart, StringComparison.Ordinal);
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

    private static ChromeTab Tab(string id, string title, string url) => new()
    {
        Id = id,
        Title = title,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://fake/{id}"
    };
}