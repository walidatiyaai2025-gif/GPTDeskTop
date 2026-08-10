using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorLiveTargetRevalidationTests
{
    [Fact]
    public async Task StartRejectsTargetThatNavigatedToAnotherConversationBeforeGateCompletion()
    {
        var root = CreateTempRoot();
        try
        {
            const string savedUrl = "https://chatgpt.com/c/live-target-original";
            var requested = Tab("target-1", "Requested", savedUrl, "ws://127.0.0.1:1/devtools/page/requested");
            var navigated = Tab("target-1", "Different chat", "https://chatgpt.com/c/live-target-different", "ws://127.0.0.1:1/devtools/page/current");
            var (service, database) = await CreateServiceAsync(root, navigated);
            var persisted = Monitor(savedUrl);
            await database.SaveMonitorAsync(persisted);
            var activity = CaptureActivity(service);

            await service.StartMonitorAsync(persisted, requested);

            Assert.False(service.IsMonitorRunning(persisted.Id));
            Assert.Contains(activity, message => message.Contains("navigated to a different conversation", StringComparison.OrdinalIgnoreCase));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == persisted.Id);
            Assert.Equal("saved-target", saved.TabId);
            Assert.Equal("Saved title", saved.Title);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StartRejectsTargetThatDisappearedBeforeGateCompletion()
    {
        var root = CreateTempRoot();
        try
        {
            const string savedUrl = "https://chatgpt.com/c/live-target-missing";
            var requested = Tab("target-missing", "Requested", savedUrl, "ws://127.0.0.1:1/devtools/page/requested");
            var (service, database) = await CreateServiceAsync(root);
            var persisted = Monitor(savedUrl);
            await database.SaveMonitorAsync(persisted);
            var activity = CaptureActivity(service);

            await service.StartMonitorAsync(persisted, requested);

            Assert.False(service.IsMonitorRunning(persisted.Id));
            Assert.Contains(activity, message => message.Contains("target disappeared before Start", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StartCommitsAndUsesFreshSameConversationTargetMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            const string savedUrl = "https://chatgpt.com/c/live-target-fresh";
            var requested = Tab("target-fresh", "Stale title", savedUrl, "ws://127.0.0.1:1/devtools/page/stale");
            var live = Tab("target-fresh", "Fresh live title", savedUrl, "ws://127.0.0.1:1/devtools/page/fresh");
            var (service, database) = await CreateServiceAsync(root, live);
            var persisted = Monitor(savedUrl);
            await database.SaveMonitorAsync(persisted);
            var activity = CaptureActivity(service);

            await service.StartMonitorAsync(persisted, requested);

            Assert.Contains(activity, message => message.Contains("Started: Fresh live title", StringComparison.Ordinal));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == persisted.Id);
            Assert.Equal("target-fresh", saved.TabId);
            Assert.Equal("Fresh live title", saved.Title);
            Assert.Equal(savedUrl, saved.Url);

            if (service.IsMonitorRunning(persisted.Id))
                await service.StopMonitorAsync(persisted.Id);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void StartSourceContractRevalidatesAndCommitsFreshTargetInsideLifecycleGate()
    {
        var service = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = Slice(service, "public async Task StartMonitorAsync", "public async Task<bool> UpdateMonitorConfigurationAsync");
        var mainForm = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var uiStart = Slice(mainForm, "private async Task StartMonitorAsync", "private ChromeTab? ResolveTab");

        var leaseIndex = start.IndexOf("AcquireLifecycleGateAsync(monitor.Id)", StringComparison.Ordinal);
        var persistedIndex = start.IndexOf("var persistedMonitor =", StringComparison.Ordinal);
        var liveReadIndex = start.IndexOf("await _chrome.GetTabsAsync()", StringComparison.Ordinal);
        var liveIdentityIndex = start.IndexOf("ChatGptConversationIdentity.IsSame(persistedMonitor.Url, liveTab.Url)", StringComparison.Ordinal);
        var targetUpdateIndex = start.IndexOf("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", StringComparison.Ordinal);
        var workerIndex = start.IndexOf("MonitorLoopAsync(persistedMonitor, liveTab, cts.Token)", StringComparison.Ordinal);
        var runningAddIndex = start.IndexOf("_running.Add", StringComparison.Ordinal);

        Assert.True(leaseIndex >= 0);
        Assert.True(persistedIndex > leaseIndex);
        Assert.True(liveReadIndex > persistedIndex);
        Assert.True(liveIdentityIndex > liveReadIndex);
        Assert.True(targetUpdateIndex > liveIdentityIndex);
        Assert.True(workerIndex > targetUpdateIndex);
        Assert.True(runningAddIndex > targetUpdateIndex);
        Assert.Contains("string.Equals(candidate.Id, tab.Id, StringComparison.Ordinal)", start, StringComparison.Ordinal);
        Assert.Contains("persistedMonitor.TabId = liveTab.Id;", start, StringComparison.Ordinal);
        Assert.Contains("persistedMonitor.Title = liveTab.Title;", start, StringComparison.Ordinal);

        Assert.Contains("await _monitor.StartMonitorAsync(monitor, tab);", uiStart, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateMonitorRuntimeTargetIfConversationMatchesAsync", uiStart, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.TabId = tab.Id", uiStart, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.Title = tab.Title", uiStart, StringComparison.Ordinal);
    }

    [Fact]
    public void StartActivityRegressionCollectorsUseThreadSafeCaptureHelper()
    {
        var source = ReadSource("tests", "GPTDeskTop.RuntimeTests", "MonitorLiveTargetRevalidationTests.cs");
        var unsafeCollector = "new List" + "<string>()";
        var unsafeSubscription = "activity." + "Add(message)";

        Assert.Contains("ConcurrentQueue<string>", source, StringComparison.Ordinal);
        Assert.Contains("CaptureActivity(service)", source, StringComparison.Ordinal);
        Assert.Contains("activity.Enqueue(message)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeCollector, source, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeSubscription, source, StringComparison.Ordinal);
    }

    private static ConcurrentQueue<string> CaptureActivity(ChatGptMonitorService service)
    {
        var activity = new ConcurrentQueue<string>();
        service.Activity += (_, message) => activity.Enqueue(message);
        return activity;
    }

    private static async Task<(ChatGptMonitorService Service, LocalDatabase Database)> CreateServiceAsync(string root, params ChromeTab[] liveTabs)
    {
        var database = new LocalDatabase(Path.Combine(root, "test.db"));
        await database.InitializeAsync();
        var chrome = new ChromeDevToolsService(new HttpClient(new ChromeListHandler(liveTabs)), new ChromeConfig());
        return (new ChatGptMonitorService(chrome, database, new MonitoringConfig()), database);
    }

    private static SavedMonitor Monitor(string url) => new()
    {
        TabId = "saved-target",
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

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts))));

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
