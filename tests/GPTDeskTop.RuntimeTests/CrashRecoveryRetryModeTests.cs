using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryRetryModeTests
{
    [Fact]
    public async Task FreshCrashResetStillPerformsGlobalTeardown()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(
                database,
                "fresh",
                "https://chatgpt.com/c/fresh-reset",
                "fresh-tab");
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "fresh-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "recover");

            var runtime = new FakeRuntime(
                [Tab("fresh-tab", monitor.Url)],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.FreshCrashReset);

            Assert.Equal(1, runtime.StopAllCount);
            Assert.Equal(1, runtime.CloseAllCount);
            Assert.Equal(monitor.Url, runtime.LaunchedUrl);
            Assert.Single(runtime.Deliveries);
            Assert.Single(runtime.StartedMonitors);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task FreshCrashResetWaitsForDevToolsEndpointAndIgnoresAbortedCloseSession()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(
                database,
                "cold-start",
                "https://chatgpt.com/c/cold-start",
                "cold-start-tab");
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "cold-start-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "recover");

            var runtime = new FakeRuntime(
                [Tab("cold-start-tab", monitor.Url)],
                (_, _) => true,
                transientGetTabsFailures: 2,
                closeThrowsTransient: true);

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.FreshCrashReset);

            Assert.Equal(1, runtime.StopAllCount);
            Assert.Equal(1, runtime.CloseAllCount);
            Assert.Equal(1, runtime.LaunchCount);
            Assert.Equal(monitor.Url, runtime.LaunchedUrl);
            Assert.Equal(3, runtime.GetTabsCalls);
            Assert.Single(runtime.Deliveries);
            Assert.Single(runtime.StartedMonitors);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task PendingRetryLaunchesChromeWhenDevToolsEndpointIsRefusedAndThenContinues()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(
                database,
                "retry-cold-start",
                "https://chatgpt.com/c/retry-cold-start",
                "retry-cold-start-tab");
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "retry-cold-start-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "recover");

            var runtime = new FakeRuntime(
                [Tab("retry-cold-start-tab", monitor.Url)],
                (_, _) => true,
                transientGetTabsFailures: 1);

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Equal(0, runtime.StopAllCount);
            Assert.Equal(0, runtime.CloseAllCount);
            Assert.Equal(1, runtime.LaunchCount);
            Assert.Equal(monitor.Url, runtime.LaunchedUrl);
            Assert.Equal(2, runtime.GetTabsCalls);
            Assert.Single(runtime.Deliveries);
            Assert.Single(runtime.StartedMonitors);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CleanPendingRetryReusesVerifiedTabWithoutGlobalTeardownOrResend()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var verified = await SaveMonitorAsync(
                database,
                "verified",
                "https://chatgpt.com/c/already-verified",
                "verified-tab");
            var invalid = await SaveMonitorAsync(
                database,
                "invalid",
                "https://chatgpt.com/",
                "invalid-tab");
            const string recoveryId = "pending-retry-incident";
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", recoveryId);
            await database.SetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{verified.Id}.Success", "1");

            var runtime = new FakeRuntime(
                [Tab("verified-tab", verified.Url)],
                (_, _) => throw new InvalidOperationException("Verified monitor must not be resent."));

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Equal(0, runtime.StopAllCount);
            Assert.Equal(0, runtime.CloseAllCount);
            Assert.Null(runtime.LaunchedUrl);
            Assert.Empty(runtime.CreatedUrls);
            Assert.Empty(runtime.Deliveries);
            var started = Assert.Single(runtime.StartedMonitors);
            Assert.Equal(verified.Id, started.MonitorId);
            Assert.Equal("verified-tab", started.TabId);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Null(await database.GetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{invalid.Id}.Success"));

            var logs = await database.GetRecentLogsAsync(50);
            Assert.Contains(logs, x => x.MonitorId == verified.Id && x.Status == "CrashRecoveryAlreadyVerifiedReused");
            Assert.Contains(logs, x => x.MonitorId == invalid.Id && x.Status == "CrashRecoveryInvalidConversationIdentity");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CleanPendingRetryMessagesOnlyUnresolvedMonitorAndReusesBothTabs()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var verified = await SaveMonitorAsync(
                database,
                "verified",
                "https://chatgpt.com/c/retry-verified",
                "verified-tab");
            var unresolved = await SaveMonitorAsync(
                database,
                "unresolved",
                "https://chatgpt.com/c/retry-unresolved",
                "unresolved-tab");
            const string recoveryId = "selective-retry-incident";
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", recoveryId);
            await database.SetSettingAsync("TimeoutRecoveryMessage", "retry-message");
            await database.SetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{verified.Id}.Success", "1");

            var runtime = new FakeRuntime(
                [
                    Tab("verified-tab", verified.Url),
                    Tab("unresolved-tab", unresolved.Url)
                ],
                (tab, _) => tab.Id == "unresolved-tab");

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Equal(0, runtime.StopAllCount);
            Assert.Equal(0, runtime.CloseAllCount);
            Assert.Null(runtime.LaunchedUrl);
            Assert.Empty(runtime.CreatedUrls);
            var delivery = Assert.Single(runtime.Deliveries);
            Assert.Equal("unresolved-tab", delivery.TabId);
            Assert.Equal("retry-message", delivery.Message);
            Assert.Equal(2, runtime.StartedMonitors.Count);
            Assert.Contains(runtime.StartedMonitors, x => x.MonitorId == verified.Id && x.TabId == "verified-tab");
            Assert.Contains(runtime.StartedMonitors, x => x.MonitorId == unresolved.Id && x.TabId == "unresolved-tab");
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal("1", await database.GetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{unresolved.Id}.Success"));
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
        string tabId)
    {
        var monitor = new SavedMonitor
        {
            Title = title,
            Url = url,
            TabId = tabId,
            AutoReply = "كمل",
            Enabled = true
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
        private readonly IReadOnlyList<ChromeTab> _tabs;
        private readonly Func<ChromeTab, string, bool> _send;
        private readonly int _transientGetTabsFailures;
        private readonly bool _closeThrowsTransient;

        public FakeRuntime(
            IReadOnlyList<ChromeTab> tabs,
            Func<ChromeTab, string, bool> send,
            int transientGetTabsFailures = 0,
            bool closeThrowsTransient = false)
        {
            _tabs = tabs;
            _send = send;
            _transientGetTabsFailures = transientGetTabsFailures;
            _closeThrowsTransient = closeThrowsTransient;
        }

        public int StopAllCount { get; private set; }
        public int CloseAllCount { get; private set; }
        public int LaunchCount { get; private set; }
        public int GetTabsCalls { get; private set; }
        public string? LaunchedUrl { get; private set; }
        public List<string> CreatedUrls { get; } = [];
        public List<(string TabId, string Message)> Deliveries { get; } = [];
        public List<(long MonitorId, string TabId)> StartedMonitors { get; } = [];

        public Task StopAllMonitorsAsync()
        {
            StopAllCount++;
            return Task.CompletedTask;
        }

        public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken)
        {
            CloseAllCount++;
            if (_closeThrowsTransient)
                throw new IOException("Chrome DevTools session is not open (state: Aborted).");
            return Task.CompletedTask;
        }

        public void LaunchMonitorChrome(string? startUrl)
        {
            LaunchCount++;
            LaunchedUrl = startUrl;
        }

        public Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
        {
            GetTabsCalls++;
            if (GetTabsCalls <= _transientGetTabsFailures)
                throw new HttpRequestException(
                    "No connection could be made because the target machine actively refused it. (127.0.0.1:9222)");
            return Task.FromResult(_tabs);
        }

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
