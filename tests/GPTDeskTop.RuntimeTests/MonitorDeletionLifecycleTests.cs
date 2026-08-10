using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorDeletionLifecycleTests
{
    [Fact]
    public async Task DirectStartIgnoresMonitorThatNoLongerExistsInSqlite()
    {
        var (service, _) = await CreateServiceAsync();
        var monitor = Monitor(991, "https://chatgpt.com/c/deleted-monitor");
        var activity = new List<string>();
        service.Activity += (_, message) => activity.Add(message);

        await service.StartMonitorAsync(monitor, Tab("deleted-tab", monitor.Url));

        Assert.False(service.IsMonitorRunning(monitor.Id));
        Assert.Contains(activity, message => message.Contains("no longer exists in SQLite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DirectStartIgnoresStaleConversationIdentityAfterPersistedRebind()
    {
        var (service, database) = await CreateServiceAsync();
        var persisted = Monitor(0, "https://chatgpt.com/c/persisted-conversation");
        await database.SaveMonitorAsync(persisted);
        var stale = Monitor(persisted.Id, "https://chatgpt.com/c/stale-conversation");
        var activity = new List<string>();
        service.Activity += (_, message) => activity.Add(message);

        await service.StartMonitorAsync(stale, Tab("stale-tab", stale.Url));

        Assert.False(service.IsMonitorRunning(stale.Id));
        Assert.Contains(activity, message => message.Contains("conversation identity changed before Start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SerializedDeleteRemovesStoppedPersistedMonitor()
    {
        var (service, database) = await CreateServiceAsync();
        var persisted = Monitor(0, "https://chatgpt.com/c/delete-boundary");
        await database.SaveMonitorAsync(persisted);

        await service.DeleteMonitorAsync(persisted.Id);

        Assert.DoesNotContain(await database.GetSavedMonitorsAsync(), monitor => monitor.Id == persisted.Id);
        Assert.False(service.IsMonitorRunning(persisted.Id));
    }

    [Fact]
    public void DeleteAndStartShareLifecycleGateAndUiUsesOneSerializedBoundary()
    {
        var serviceSource = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var start = Slice(serviceSource, "public async Task StartMonitorAsync", "public async Task StopMonitorAsync");
        var stop = Slice(serviceSource, "public async Task StopMonitorAsync", "public async Task DeleteMonitorAsync");
        var delete = Slice(serviceSource, "public async Task DeleteMonitorAsync", "private async Task StopMonitorCoreAsync");
        var stopCore = Slice(serviceSource, "private async Task StopMonitorCoreAsync", "public async Task StopAllAsync");

        Assert.Contains("AcquireLifecycleGateAsync(monitor.Id)", start, StringComparison.Ordinal);
        Assert.Contains("GetSavedMonitorsAsync", start, StringComparison.Ordinal);
        Assert.Contains("candidate.Id == monitor.Id", start, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(persistedMonitor.Url, monitor.Url)", start, StringComparison.Ordinal);
        Assert.True(start.IndexOf("persistedMonitor is null", StringComparison.Ordinal) < start.IndexOf("_running.Add", StringComparison.Ordinal));

        Assert.Contains("AcquireLifecycleGateAsync(monitorId)", stop, StringComparison.Ordinal);
        Assert.Contains("await StopMonitorCoreAsync(monitorId);", stop, StringComparison.Ordinal);
        Assert.Contains("AcquireLifecycleGateAsync(monitorId)", delete, StringComparison.Ordinal);
        var leaseIndex = delete.IndexOf("using var lifecycleLease = await AcquireLifecycleGateAsync(monitorId);", StringComparison.Ordinal);
        var deleteStopIndex = delete.IndexOf("await StopMonitorCoreAsync(monitorId);", StringComparison.Ordinal);
        var persistedDeleteIndex = delete.IndexOf("await _database.DeleteMonitorAsync(monitorId);", StringComparison.Ordinal);
        Assert.True(leaseIndex >= 0 && deleteStopIndex > leaseIndex && persistedDeleteIndex > deleteStopIndex);

        Assert.Contains("runtime.StopOwnsCleanup = true;", stopCore, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, runtime)", stopCore, StringComparison.Ordinal);

        var mainFormSource = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "MainForm.cs"));
        var uiDelete = Slice(mainFormSource, "private async Task DeleteSelectedMonitorAsync", "private async Task StartSelectedMonitorAsync");
        Assert.Contains("await _monitor.DeleteMonitorAsync(id);", uiDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.StopMonitorAsync", uiDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("_database.DeleteMonitorAsync", uiDelete, StringComparison.Ordinal);
    }

    private static async Task<(ChatGptMonitorService Service, LocalDatabase Database)> CreateServiceAsync()
    {
        var database = new LocalDatabase(Path.Combine(
            Path.GetTempPath(),
            $"gptdesktop-monitor-delete-{Guid.NewGuid():N}.db"));
        await database.InitializeAsync();
        var chrome = new ChromeDevToolsService(new HttpClient(), new ChromeConfig());
        return (new ChatGptMonitorService(chrome, database, new MonitoringConfig()), database);
    }

    private static SavedMonitor Monitor(long id, string url) => new()
    {
        Id = id,
        TabId = $"saved-{id}",
        Title = $"Monitor {id}",
        Url = url,
        AutoReply = "كمل",
        ReplyDelaySeconds = 0,
        TimerSeconds = 1,
        Enabled = true
    };

    private static ChromeTab Tab(string id, string url) => new()
    {
        Id = id,
        Title = id,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://fake/{id}"
    };

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
