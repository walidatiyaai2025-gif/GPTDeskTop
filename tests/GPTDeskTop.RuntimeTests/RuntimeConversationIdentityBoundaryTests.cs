using System.Net;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeConversationIdentityBoundaryTests
{
    [Fact]
    public async Task DirectMonitorStartRejectsInvalidSavedIdentityBeforeWorkerCreation()
    {
        var service = CreateMonitorService();
        var monitor = new SavedMonitor
        {
            Id = 41,
            Title = "Legacy home monitor",
            Url = "https://chatgpt.com/",
            AutoReply = "كمل"
        };
        var tab = Tab("tab-home", "https://chatgpt.com/");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartMonitorAsync(monitor, tab));

        Assert.Contains("saved monitor URL", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.IsMonitorRunning(monitor.Id));
    }

    [Fact]
    public async Task DirectMonitorStartDefersInvalidMutableLiveTabWithoutWorkerOrException()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "runtime-boundary.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "tab-home",
                Title = "Valid saved conversation",
                Url = "https://chatgpt.com/c/runtime-boundary-valid",
                AutoReply = "كمل",
                Enabled = true
            };
            await database.SaveMonitorAsync(monitor);
            var tab = Tab("tab-home", "https://chatgpt.com/");
            var chrome = new ChromeDevToolsService(
                new HttpClient(new ChromeListHandler(tab)),
                new ChromeConfig());
            var service = new ChatGptMonitorService(chrome, database, new MonitoringConfig());
            var activity = new List<string>();
            service.Activity += (_, message) => activity.Add(message);

            var exception = await Record.ExceptionAsync(() => service.StartMonitorAsync(monitor, tab));

            Assert.Null(exception);
            Assert.False(service.IsMonitorRunning(monitor.Id));
            Assert.Contains(activity, message => message.Contains("no longer exposes a stable ChatGPT conversation", StringComparison.OrdinalIgnoreCase));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("https://chatgpt.com/c/runtime-boundary-valid", saved.Url);
            Assert.Equal("tab-home", saved.TabId);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void DevelopmentResolverRejectsInvalidPersistedUrlBeforeExactTabIdMatch()
    {
        var monitor = new SavedMonitor
        {
            Id = 43,
            TabId = "persisted-tab",
            Title = "Invalid legacy monitor",
            Url = "https://chatgpt.com/"
        };
        var tabs = new[]
        {
            Tab("persisted-tab", "https://chatgpt.com/c/different-valid-conversation")
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.False(result.Found);
        Assert.Null(result.Tab);
        Assert.Equal("None", result.MatchType);
        Assert.Contains("not a stable ChatGPT conversation identity", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ChatGptMonitorService CreateMonitorService()
    {
        var database = new LocalDatabase(Path.Combine(
            Path.GetTempPath(),
            $"gptdesktop-runtime-boundary-{Guid.NewGuid():N}.db"));
        var chrome = new ChromeDevToolsService(new HttpClient(), new ChromeConfig());
        return new ChatGptMonitorService(chrome, database, new MonitoringConfig());
    }

    private static ChromeTab Tab(string id, string url) => new()
    {
        Id = id,
        Title = id,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://fake/{id}"
    };

    private sealed class ChromeListHandler : HttpMessageHandler
    {
        private readonly ChromeTab[] _tabs;

        public ChromeListHandler(params ChromeTab[] tabs)
            => _tabs = tabs;

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