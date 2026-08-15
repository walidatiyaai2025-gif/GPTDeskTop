using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ManualStartStaleTargetRegressionTests
{
    [Fact]
    public async Task NonStableCallerSnapshotRebindsToFreshSavedConversationTargetWithoutThrowing()
    {
        var root = CreateTempRoot();
        try
        {
            const string savedUrl = "https://chatgpt.com/c/starttab-saved-conversation";
            var staleCaller = Tab(
                "old-target",
                "ChatGPT",
                "https://chatgpt.com/",
                "ws://127.0.0.1:1/devtools/page/old");
            var movedConversation = Tab(
                "moved-target",
                "Saved conversation",
                savedUrl,
                "ws://127.0.0.1:1/devtools/page/moved");
            var (service, database) = await CreateServiceAsync(root, staleCaller, movedConversation);
            var monitor = Monitor(savedUrl, "old-target");
            await database.SaveMonitorAsync(monitor);
            var activity = CaptureActivity(service);

            var exception = await Record.ExceptionAsync(() => service.StartMonitorAsync(monitor, staleCaller));

            Assert.Null(exception);
            Assert.Contains(activity, message => message.Contains("Started: Saved conversation", StringComparison.Ordinal));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal(savedUrl, saved.Url);
            Assert.Equal("moved-target", saved.TabId);
            Assert.Equal("Saved conversation", saved.Title);

            if (service.IsMonitorRunning(monitor.Id))
                await service.StopMonitorAsync(monitor.Id);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task NonStableCallerWithoutMatchingConversationDefersWithoutUiExceptionOrOwnershipChange()
    {
        var root = CreateTempRoot();
        try
        {
            const string savedUrl = "https://chatgpt.com/c/starttab-missing-conversation";
            var staleCaller = Tab(
                "old-target",
                "ChatGPT",
                "https://chatgpt.com/",
                "ws://127.0.0.1:1/devtools/page/old");
            var (service, database) = await CreateServiceAsync(root, staleCaller);
            var monitor = Monitor(savedUrl, "old-target");
            await database.SaveMonitorAsync(monitor);
            var activity = CaptureActivity(service);

            var exception = await Record.ExceptionAsync(() => service.StartMonitorAsync(monitor, staleCaller));

            Assert.Null(exception);
            Assert.False(service.IsMonitorRunning(monitor.Id));
            Assert.Contains(activity, message => message.Contains("no longer exposes a stable ChatGPT conversation", StringComparison.OrdinalIgnoreCase));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal(savedUrl, saved.Url);
            Assert.Equal("old-target", saved.TabId);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void StartSourceTreatsCallerTabAsLocatorHintAndFreshConversationIdentityAsAuthority()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var start = Slice(source, "public async Task StartMonitorAsync", "public async Task<bool> UpdateMonitorConfigurationAsync");

        Assert.DoesNotContain(
            "The selected Chrome tab is not a stable ChatGPT conversation identity.",
            start,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "The selected Chrome target no longer represents the saved ChatGPT conversation identity.",
            start,
            StringComparison.Ordinal);
        Assert.Contains("using var lifecycleLease = await AcquireLifecycleGateAsync(monitor.Id)", start, StringComparison.Ordinal);
        Assert.Contains("var liveTabs = await _chrome.GetTabsAsync()", start, StringComparison.Ordinal);
        Assert.Contains("requestedLiveTab = liveTabs.FirstOrDefault", start, StringComparison.Ordinal);
        Assert.Contains(
            "liveTabs.FirstOrDefault(candidate => ChatGptConversationIdentity.IsSame(persistedMonitor.Url, candidate.Url))",
            start,
            StringComparison.Ordinal);
        Assert.Contains("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", start, StringComparison.Ordinal);
    }

    private static ConcurrentQueue<string> CaptureActivity(ChatGptMonitorService service)
    {
        var activity = new ConcurrentQueue<string>();
        service.Activity += (_, message) => activity.Enqueue(message);
        return activity;
    }

    private static async Task<(ChatGptMonitorService Service, LocalDatabase Database)> CreateServiceAsync(
        string root,
        params ChromeTab[] liveTabs)
    {
        var database = new LocalDatabase(Path.Combine(root, "test.db"));
        await database.InitializeAsync();
        var chrome = new ChromeDevToolsService(new HttpClient(new ChromeListHandler(liveTabs)), new ChromeConfig());
        return (new ChatGptMonitorService(chrome, database, new MonitoringConfig()), database);
    }

    private static SavedMonitor Monitor(string url, string tabId) => new()
    {
        TabId = tabId,
        Title = "Saved title",
        Url = url,
        AutoReply = "كمل",
        ReplyDelaySeconds = 0,
        TimerSeconds = 1,
        Enabled = true
    };

    private static ChromeTab Tab(string id, string title, string url, string webSocketDebuggerUrl) => new()
    {
        Id = id,
        Title = title,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = webSocketDebuggerUrl
    };

    private static string RepositoryPath(params string[] parts)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));

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

    private sealed class ChromeListHandler : HttpMessageHandler
    {
        private readonly ChromeTab[] _tabs;

        public ChromeListHandler(IEnumerable<ChromeTab> tabs)
            => _tabs = tabs.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(_tabs.Select(tab => new
            {
                id = tab.Id,
                title = tab.Title,
                url = tab.Url,
                type = tab.Type,
                webSocketDebuggerUrl = tab.WebSocketDebuggerUrl
            }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}