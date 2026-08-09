using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryConversationIdentityTests
{
    [Fact]
    public async Task InvalidMonitorIsNeverOpenedOrMessagedAndKeepsRecoveryPending()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();

            var invalid = await SaveMonitorAsync(database, "invalid-home", "https://chatgpt.com/", enabled: true);
            var valid = await SaveMonitorAsync(database, "valid-chat", "https://chatgpt.com/c/recovery-valid", enabled: true);
            const string recoveryId = "incident-invalid-identity";
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", recoveryId);
            await database.SetSettingAsync("TimeoutRecoveryMessage", "continue safely");

            var runtime = new FakeRuntime(
                [Tab("valid-tab", valid.Url)],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(runtime, database);

            Assert.Equal(valid.Url, runtime.LaunchedUrl);
            Assert.DoesNotContain(invalid.Url, runtime.CreatedUrls);
            Assert.Single(runtime.Deliveries);
            Assert.Equal("valid-tab", runtime.Deliveries[0].TabId);
            Assert.Equal("continue safely", runtime.Deliveries[0].Message);
            Assert.Single(runtime.StartedMonitors);
            Assert.Equal(valid.Id, runtime.StartedMonitors[0].MonitorId);

            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal(recoveryId, await database.GetSettingAsync("CrashRecovery.RecoveryId"));
            Assert.Null(await database.GetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{invalid.Id}.Success"));
            Assert.Equal("1", await database.GetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{valid.Id}.Success"));

            var logs = await database.GetRecentLogsAsync(50);
            var invalidLog = Assert.Single(logs, x =>
                x.MonitorId == invalid.Id
                && x.Status == "CrashRecoveryInvalidConversationIdentity");
            Assert.Contains("not a stable ChatGPT conversation identity", invalidLog.Response, StringComparison.OrdinalIgnoreCase);

            var retry = new FakeRuntime(
                [Tab("valid-retry-tab", valid.Url)],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(retry, database);

            Assert.Empty(retry.Deliveries);
            Assert.Single(retry.StartedMonitors);
            Assert.Equal(valid.Id, retry.StartedMonitors[0].MonitorId);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task AllInvalidMonitorsDoNotLaunchChromeOrCreateTabs()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            await SaveMonitorAsync(database, "home", "https://chatgpt.com/", enabled: true);
            await SaveMonitorAsync(database, "share", "https://chatgpt.com/share/public-id", enabled: true);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "all-invalid");

            var runtime = new FakeRuntime([], (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(runtime, database);

            Assert.Null(runtime.LaunchedUrl);
            Assert.Empty(runtime.CreatedUrls);
            Assert.Empty(runtime.Deliveries);
            Assert.Empty(runtime.StartedMonitors);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));

            var logs = await database.GetRecentLogsAsync(50);
            Assert.Equal(2, logs.Count(x => x.Status == "CrashRecoveryInvalidConversationIdentity"));
            Assert.Contains(logs, x => x.Status == "CrashRecoveryPartialFailure");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<SavedMonitor> SaveMonitorAsync(
        LocalDatabase database,
        string title,
        string url,
        bool enabled)
    {
        var monitor = new SavedMonitor
        {
            Title = title,
            Url = url,
            TabId = $"old-{title}",
            AutoReply = "كمل",
            Enabled = enabled
        };
        monitor.Id = await database.SaveMonitorAsync(monitor);
        return monitor;
    }

    private static ChromeTab Tab(string id, string url) => new()
    {
        Id = id,
        Title = id,
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://fake/{id}"
    };

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "GPTDeskTop.RuntimeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }

    private sealed class FakeRuntime : ICrashRecoveryRuntime
    {
        private readonly IReadOnlyList<ChromeTab> _initialTabs;
        private readonly Func<ChromeTab, string, bool> _send;

        public FakeRuntime(
            IReadOnlyList<ChromeTab> initialTabs,
            Func<ChromeTab, string, bool> send)
        {
            _initialTabs = initialTabs;
            _send = send;
        }

        public string? LaunchedUrl { get; private set; }
        public List<string> CreatedUrls { get; } = [];
        public List<(string TabId, string Message)> Deliveries { get; } = [];
        public List<(long MonitorId, string TabId)> StartedMonitors { get; } = [];

        public Task StopAllMonitorsAsync() => Task.CompletedTask;
        public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void LaunchMonitorChrome(string? startUrl) => LaunchedUrl = startUrl;

        public Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
            => Task.FromResult(_initialTabs);

        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
        {
            CreatedUrls.Add(url);
            return Task.FromResult(Tab($"created-{CreatedUrls.Count}", url));
        }

        public Task<bool> SendChatMessageVerifiedAsync(
            ChromeTab tab,
            string message,
            CancellationToken cancellationToken)
        {
            Deliveries.Add((tab.Id, message));
            return Task.FromResult(_send(tab, message));
        }

        public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
        {
            StartedMonitors.Add((monitor.Id, tab.Id));
            return Task.CompletedTask;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
