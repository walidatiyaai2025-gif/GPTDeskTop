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
    public async Task DirectMonitorStartRejectsInvalidLiveTabBeforeWorkerCreation()
    {
        var service = CreateMonitorService();
        var monitor = new SavedMonitor
        {
            Id = 42,
            Title = "Valid saved conversation",
            Url = "https://chatgpt.com/c/runtime-boundary-valid",
            AutoReply = "كمل"
        };
        var tab = Tab("tab-home", "https://chatgpt.com/");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartMonitorAsync(monitor, tab));

        Assert.Contains("selected Chrome tab", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.IsMonitorRunning(monitor.Id));
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
}
