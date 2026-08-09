using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoverySingleFlightTests
{
    [Fact]
    public async Task ConcurrentPendingRetryWaitsForFirstPassAndDoesNotDeliverAfterPendingClears()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "single-flight-incident");
            await database.SetSettingAsync("TimeoutRecoveryMessage", "continue");

            await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "tab-single-flight",
                Title = "Single flight",
                Url = "https://chatgpt.com/c/single-flight-test",
                Enabled = false
            });

            var runtime = new BlockingRuntime();
            var first = CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            await runtime.FirstGetTabsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            await Task.Delay(100);
            Assert.Equal(1, runtime.GetTabsCalls);
            Assert.False(second.IsCompleted);

            runtime.ReleaseFirstGetTabs.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await second.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
            Assert.Equal(1, runtime.GetTabsCalls);
            Assert.Equal(1, runtime.SendCalls);
            Assert.Equal(0, runtime.StopAllCalls);
            Assert.Equal(0, runtime.CloseAllCalls);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private sealed class BlockingRuntime : ICrashRecoveryRuntime
    {
        public TaskCompletionSource FirstGetTabsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstGetTabs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GetTabsCalls { get; private set; }
        public int SendCalls { get; private set; }
        public int StopAllCalls { get; private set; }
        public int CloseAllCalls { get; private set; }

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

        public void LaunchMonitorChrome(string? startUrl) { }

        public async Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
        {
            GetTabsCalls++;
            if (GetTabsCalls == 1)
            {
                FirstGetTabsEntered.TrySetResult();
                await ReleaseFirstGetTabs.Task.WaitAsync(cancellationToken);
            }

            return new[]
            {
                new ChromeTab
                {
                    Id = "tab-single-flight",
                    Title = "Single flight",
                    Url = "https://chatgpt.com/c/single-flight-test"
                }
            };
        }

        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(new ChromeTab { Id = "created", Title = "Created", Url = url });

        public Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(true);
        }

        public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab) => Task.CompletedTask;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
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