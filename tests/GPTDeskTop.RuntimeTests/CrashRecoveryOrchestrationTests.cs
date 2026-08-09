using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryOrchestrationTests
{
    [Fact]
    public async Task EveryRecoveredTabReceivesConfiguredMessageAndOnlyEnabledMonitorsRestart()
    {
        var root = CreateRoot();
        try
        {
            var database = await CreateDatabaseAsync(root);
            var first = await SaveMonitorAsync(database, "first", "https://chatgpt.com/c/first", enabled: true);
            var second = await SaveMonitorAsync(database, "second", "https://chatgpt.com/c/second", enabled: false);
            var third = await SaveMonitorAsync(database, "third", "https://chatgpt.com/c/third", enabled: true);
            await ArmRecoveryAsync(database, "incident-all-success", "كمل");

            var runtime = new FakeCrashRecoveryRuntime(
                [Tab("new-first", first.Url)],
                [Tab("new-second", second.Url), Tab("new-third", third.Url)],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(runtime, database);

            Assert.Equal(1, runtime.StopAllCalls);
            Assert.Equal(1, runtime.CloseAllCalls);
            Assert.Equal(first.Url, runtime.LaunchedUrl);
            Assert.Equal(3, runtime.Deliveries.Count);
            Assert.All(runtime.Deliveries, delivery => Assert.Equal("كمل", delivery.Message));
            Assert.Equal(
                ["new-first", "new-second", "new-third"],
                runtime.Deliveries.Select(x => x.TabId).ToArray());
            Assert.Equal(
                [first.Id, third.Id],
                runtime.StartedMonitors.Select(x => x.MonitorId).ToArray());

            var recovered = await database.GetSavedMonitorsAsync();
            Assert.Equal("new-first", recovered.Single(x => x.Id == first.Id).TabId);
            Assert.Equal("new-second", recovered.Single(x => x.Id == second.Id).TabId);
            Assert.Equal("new-third", recovered.Single(x => x.Id == third.Id).TabId);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal(string.Empty, await database.GetSettingAsync("CrashRecovery.RecoveryId"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task PartialFailureKeepsPendingAndRetryDoesNotResendAlreadyVerifiedMonitor()
    {
        var root = CreateRoot();
        try
        {
            var database = await CreateDatabaseAsync(root);
            var first = await SaveMonitorAsync(database, "first", "https://chatgpt.com/c/first", enabled: true);
            var second = await SaveMonitorAsync(database, "second", "https://chatgpt.com/c/second", enabled: true);
            const string recoveryId = "incident-partial";
            await ArmRecoveryAsync(database, recoveryId, "continue");

            var firstAttempt = new FakeCrashRecoveryRuntime(
                [Tab("attempt1-first", first.Url)],
                [Tab("attempt1-second", second.Url)],
                (tab, _) => !string.Equals(tab.Id, "attempt1-second", StringComparison.Ordinal));

            await CrashRecoveryService.RecoverIfPendingAsync(firstAttempt, database);

            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal(recoveryId, await database.GetSettingAsync("CrashRecovery.RecoveryId"));
            Assert.Single(firstAttempt.StartedMonitors);
            Assert.Equal(first.Id, firstAttempt.StartedMonitors[0].MonitorId);
            Assert.Equal(1, firstAttempt.Deliveries.Count(x => x.TabId == "attempt1-first"));
            Assert.Equal(6, firstAttempt.Deliveries.Count(x => x.TabId == "attempt1-second"));
            Assert.Equal(
                "1",
                await database.GetSettingAsync($"CrashRecovery.{recoveryId}.Monitor.{first.Id}.Success"));

            var retry = new FakeCrashRecoveryRuntime(
                [Tab("retry-first", first.Url)],
                [Tab("retry-second", second.Url)],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(retry, database);

            Assert.DoesNotContain(retry.Deliveries, x => x.TabId == "retry-first");
            Assert.Single(retry.Deliveries);
            Assert.Equal("retry-second", retry.Deliveries[0].TabId);
            Assert.Equal("continue", retry.Deliveries[0].Message);
            Assert.Equal(
                [first.Id, second.Id],
                retry.StartedMonitors.Select(x => x.MonitorId).ToArray());
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal(string.Empty, await database.GetSettingAsync("CrashRecovery.RecoveryId"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<LocalDatabase> CreateDatabaseAsync(string root)
    {
        var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
        await database.InitializeAsync();
        return database;
    }

    private static async Task<SavedMonitor> SaveMonitorAsync(LocalDatabase database, string title, string url, bool enabled)
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

    private static async Task ArmRecoveryAsync(LocalDatabase database, string recoveryId, string message)
    {
        await database.SetSettingAsync("CrashRecoveryPending", "1");
        await database.SetSettingAsync("CrashRecovery.RecoveryId", recoveryId);
        await database.SetSettingAsync("TimeoutRecoveryMessage", message);
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
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }

    private sealed class FakeCrashRecoveryRuntime : ICrashRecoveryRuntime
    {
        private readonly IReadOnlyList<ChromeTab> _initialTabs;
        private readonly Queue<ChromeTab> _createdTabs;
        private readonly Func<ChromeTab, string, bool> _send;

        public FakeCrashRecoveryRuntime(
            IReadOnlyList<ChromeTab> initialTabs,
            IEnumerable<ChromeTab> createdTabs,
            Func<ChromeTab, string, bool> send)
        {
            _initialTabs = initialTabs;
            _createdTabs = new Queue<ChromeTab>(createdTabs);
            _send = send;
        }

        public int StopAllCalls { get; private set; }
        public int CloseAllCalls { get; private set; }
        public string? LaunchedUrl { get; private set; }
        public List<(string TabId, string Message)> Deliveries { get; } = [];
        public List<(long MonitorId, string TabId)> StartedMonitors { get; } = [];

        public Task StopAllMonitorsAsync()
        {
            StopAllCalls++;
            return Task.CompletedTask;
        }

        public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken)
        {
            CloseAllCalls++;
            return Task.CompletedTask;
        }

        public void LaunchMonitorChrome(string? startUrl) => LaunchedUrl = startUrl;

        public Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
            => Task.FromResult(_initialTabs);

        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
        {
            if (_createdTabs.Count == 0)
                throw new InvalidOperationException("No fake crash-recovery tab remains.");
            return Task.FromResult(_createdTabs.Dequeue());
        }

        public Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken)
        {
            Deliveries.Add((tab.Id, message));
            return Task.FromResult(_send(tab, message));
        }

        public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
        {
            StartedMonitors.Add((monitor.Id, tab.Id));
            return Task.CompletedTask;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
