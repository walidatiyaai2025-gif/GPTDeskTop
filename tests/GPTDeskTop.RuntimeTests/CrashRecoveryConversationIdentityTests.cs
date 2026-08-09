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

    [Fact]
    public async Task PendingRetryRejectsReusedTargetIdAndFallsBackToSameConversationUrl()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "target-reuse", "https://chatgpt.com/c/original", enabled: true);
            monitor.TabId = "reused-target";
            await database.SaveMonitorAsync(monitor);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "target-reuse");

            var runtime = new FakeRuntime(
                [
                    Tab("reused-target", "https://chatgpt.com/c/different"),
                    Tab("correct-target", "https://chatgpt.com/c/original/")
                ],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Single(runtime.Deliveries);
            Assert.Equal("correct-target", runtime.Deliveries[0].TabId);
            Assert.Single(runtime.StartedMonitors);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("correct-target", saved.TabId);
            Assert.Equal("https://chatgpt.com/c/original", saved.Url);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CreatedTabForDifferentConversationIsRejectedWithoutSendStartOrUrlMutation()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "create-mismatch", "https://chatgpt.com/c/expected", enabled: true);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "create-mismatch");

            var runtime = new FakeRuntime(
                [],
                (_, _) => true,
                createTab: _ => Tab("redirected", "https://chatgpt.com/c/unexpected"));

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Single(runtime.CreatedUrls);
            Assert.Empty(runtime.Deliveries);
            Assert.Empty(runtime.StartedMonitors);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("https://chatgpt.com/c/expected", saved.Url);
            var logs = await database.GetRecentLogsForMonitorAsync(monitor.Id, 20);
            Assert.Contains(logs, log => log.Status == "CrashRecoveryTabIdentityMismatch");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ConcurrentSavedConversationChangeBeforeRecoverySendSkipsStaleSnapshot()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "concurrent-change", "https://chatgpt.com/c/original", enabled: true);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "concurrent-change");

            var changed = false;
            var runtime = new FakeRuntime(
                [Tab("original-target", "https://chatgpt.com/c/original")],
                (_, _) => true,
                beforeGetTabs: async () =>
                {
                    if (changed) return;
                    changed = true;
                    var current = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
                    current.Url = "https://chatgpt.com/c/repaired";
                    current.TabId = "repair-target";
                    current.Title = "Repaired";
                    await database.SaveMonitorAsync(current);
                });

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Empty(runtime.Deliveries);
            Assert.Empty(runtime.StartedMonitors);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("https://chatgpt.com/c/repaired", saved.Url);
            Assert.Equal("repair-target", saved.TabId);
            var logs = await database.GetRecentLogsForMonitorAsync(monitor.Id, 20);
            Assert.Contains(logs, log => log.Status == "CrashRecoverySavedConversationChanged");
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
        private readonly Func<string, ChromeTab>? _createTab;
        private readonly Func<Task>? _beforeGetTabs;

        public FakeRuntime(
            IReadOnlyList<ChromeTab> initialTabs,
            Func<ChromeTab, string, bool> send,
            Func<string, ChromeTab>? createTab = null,
            Func<Task>? beforeGetTabs = null)
        {
            _initialTabs = initialTabs;
            _send = send;
            _createTab = createTab;
            _beforeGetTabs = beforeGetTabs;
        }

        public string? LaunchedUrl { get; private set; }
        public List<string> CreatedUrls { get; } = [];
        public List<(string TabId, string Message)> Deliveries { get; } = [];
        public List<(long MonitorId, string TabId)> StartedMonitors { get; } = [];

        public Task StopAllMonitorsAsync() => Task.CompletedTask;
        public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void LaunchMonitorChrome(string? startUrl) => LaunchedUrl = startUrl;

        public async Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
        {
            if (_beforeGetTabs is not null)
                await _beforeGetTabs();
            return _initialTabs;
        }

        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
        {
            CreatedUrls.Add(url);
            return Task.FromResult(_createTab?.Invoke(url) ?? Tab($"created-{CreatedUrls.Count}", url));
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
