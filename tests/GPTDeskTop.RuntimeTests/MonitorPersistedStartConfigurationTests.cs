using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorPersistedStartConfigurationTests
{
    [Fact]
    public async Task StartUsesFreshPersistedConfigurationInsteadOfStaleCallerSnapshot()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, database) = await CreateServiceAsync(root);
            var persisted = Monitor(0, "https://chatgpt.com/c/fresh-start-config");
            persisted.Title = "Persisted title";
            persisted.AutoReply = "fresh-reply";
            persisted.ReplyDelaySeconds = 13;
            persisted.TimerSeconds = 7;
            await database.SaveMonitorAsync(persisted);

            var stale = Monitor(persisted.Id, persisted.Url);
            stale.Title = "Stale caller title";
            stale.AutoReply = string.Empty;
            stale.ReplyDelaySeconds = 0;
            stale.TimerSeconds = 1;

            var startupActivity = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Activity += (_, message) =>
            {
                if (message.Contains("Passive long-response wait ON", StringComparison.Ordinal))
                    startupActivity.TrySetResult(message);
            };

            await service.StartMonitorAsync(stale, Tab("fresh-start-tab", persisted.Url));
            var startup = await startupActivity.Task.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Contains("Persisted title", startup, StringComparison.Ordinal);
            Assert.Contains("Timer 7s", startup, StringComparison.Ordinal);
            Assert.Contains("Delay 13s", startup, StringComparison.Ordinal);
            Assert.Contains("Reply: fresh-reply", startup, StringComparison.Ordinal);
            Assert.DoesNotContain("Stale caller title", startup, StringComparison.Ordinal);
            Assert.True(service.IsMonitorRunning(persisted.Id));

            await service.StopMonitorAsync(persisted.Id);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SettingsSaveIsRejectedWithoutDatabaseMutationWhenMonitorIsAlreadyRunning()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, database) = await CreateServiceAsync(root);
            var persisted = Monitor(0, "https://chatgpt.com/c/running-settings-guard");
            persisted.AutoReply = "before";
            persisted.TimerSeconds = 5;
            await database.SaveMonitorAsync(persisted);

            await service.StartMonitorAsync(Monitor(persisted.Id, persisted.Url), Tab("running-settings-tab", persisted.Url));
            Assert.True(service.IsMonitorRunning(persisted.Id));

            var update = Monitor(persisted.Id, persisted.Url);
            update.AutoReply = "after";
            update.TimerSeconds = 9;
            update.ReplyDelaySeconds = 21;

            var saved = await service.UpdateMonitorConfigurationAsync(update);

            Assert.False(saved);
            var current = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == persisted.Id);
            Assert.Equal("before", current.AutoReply);
            Assert.Equal(5, current.TimerSeconds);
            Assert.NotEqual(21, current.ReplyDelaySeconds);

            await service.StopMonitorAsync(persisted.Id);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SettingsSaveBeforeStartIsLoadedByQueuedRuntime()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, database) = await CreateServiceAsync(root);
            var persisted = Monitor(0, "https://chatgpt.com/c/save-before-start");
            persisted.AutoReply = "before";
            persisted.TimerSeconds = 2;
            persisted.ReplyDelaySeconds = 1;
            await database.SaveMonitorAsync(persisted);

            var update = Monitor(persisted.Id, persisted.Url);
            update.Title = "Caller snapshot title";
            update.AutoReply = "saved-first";
            update.TimerSeconds = 6;
            update.ReplyDelaySeconds = 17;
            Assert.True(await service.UpdateMonitorConfigurationAsync(update));

            var staleStart = Monitor(persisted.Id, persisted.Url);
            staleStart.AutoReply = "stale-start";
            staleStart.TimerSeconds = 1;
            staleStart.ReplyDelaySeconds = 0;

            var startupActivity = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Activity += (_, message) =>
            {
                if (message.Contains("Passive long-response wait ON", StringComparison.Ordinal))
                    startupActivity.TrySetResult(message);
            };

            await service.StartMonitorAsync(staleStart, Tab("save-before-start-tab", persisted.Url));
            var startup = await startupActivity.Task.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Contains("Timer 6s", startup, StringComparison.Ordinal);
            Assert.Contains("Delay 17s", startup, StringComparison.Ordinal);
            Assert.Contains("Reply: saved-first", startup, StringComparison.Ordinal);
            Assert.DoesNotContain("Reply: stale-start", startup, StringComparison.Ordinal);

            await service.StopMonitorAsync(persisted.Id);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void SourceContractUsesPersistedSnapshotAndSharedLifecycleGateForSettings()
    {
        var service = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = Slice(service, "public async Task StartMonitorAsync", "public async Task<bool> UpdateMonitorConfigurationAsync");
        var update = Slice(service, "public async Task<bool> UpdateMonitorConfigurationAsync", "public async Task StopMonitorAsync");
        var mainForm = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var edit = Slice(mainForm, "private async Task EditSelectedMonitorSettingsAsync", "private async Task DeleteSelectedMonitorAsync");

        Assert.Contains("using var lifecycleLease = await AcquireLifecycleGateAsync(monitor.Id);", start, StringComparison.Ordinal);
        Assert.Contains("var persistedMonitor = savedMonitors.FirstOrDefault", start, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(persistedMonitor.AutoReply)", start, StringComparison.Ordinal);
        Assert.Contains("MonitorLoopAsync(persistedMonitor, tab, cts.Token)", start, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitorLoopAsync(monitor, tab, cts.Token)", start, StringComparison.Ordinal);

        Assert.Contains("using var lifecycleLease = await AcquireLifecycleGateAsync(monitor.Id);", update, StringComparison.Ordinal);
        Assert.Contains("_running.ContainsKey(monitor.Id)", update, StringComparison.Ordinal);
        Assert.Contains("return await _database.UpdateMonitorConfigurationAsync(monitor);", update, StringComparison.Ordinal);

        Assert.Contains("await _monitor.UpdateMonitorConfigurationAsync(_selectedMonitor)", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("_database.UpdateMonitorConfigurationAsync(_selectedMonitor)", edit, StringComparison.Ordinal);
        Assert.Contains("await RefreshMonitorsAsync()", edit, StringComparison.Ordinal);
    }

    private static async Task<(ChatGptMonitorService Service, LocalDatabase Database)> CreateServiceAsync(string root)
    {
        var database = new LocalDatabase(Path.Combine(root, "test.db"));
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
        ReplyDelaySeconds = 3,
        TimerSeconds = 1,
        Enabled = true,
        ConversationRotationEnabled = true,
        NewChatStartMessage = "كمل",
        NewChatDelaySeconds = 2,
        RotationCooldownSeconds = 3,
        MaxConversationRotations = 5,
        RotationCount = 0,
        ModelRoutingEnabled = false,
        PreferredModel = "Auto",
        FallbackModel = "Auto"
    };

    private static ChromeTab Tab(string id, string url) => new()
    {
        Id = id,
        Title = id,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://fake/{id}"
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
}
