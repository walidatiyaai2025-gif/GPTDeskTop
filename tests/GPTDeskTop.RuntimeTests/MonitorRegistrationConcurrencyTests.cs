using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorRegistrationConcurrencyTests
{
    [Fact]
    public async Task ConcurrentRegistrationsForSameConversationCreateExactlyOneRow()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var first = NewMonitor("tab-a", "First", "https://chatgpt.com/c/shared-conversation");
            var second = NewMonitor("tab-b", "Second", "https://CHATGPT.com/c/SHARED-conversation");

            var results = await Task.WhenAll(
                database.RegisterMonitorIfConversationAvailableAsync(first),
                database.RegisterMonitorIfConversationAvailableAsync(second));

            Assert.Single(results.Where(result => result.Created));
            Assert.Single(results.Where(result => !result.Created));
            Assert.Equal(results[0].MonitorId, results[1].MonitorId);
            Assert.Equal(results[0].MonitorId, first.Id);
            Assert.Equal(results[1].MonitorId, second.Id);

            var saved = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal(results[0].MonitorId, saved.Id);
            Assert.Equal("https://chatgpt.com/c/shared-conversation", saved.Url, ignoreCase: true);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task SaveMonitorForNewRowsUsesDuplicateSafeRegistrationBoundary()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var first = NewMonitor("tab-a", "First", "https://chatgpt.com/c/save-boundary");
            var second = NewMonitor("tab-b", "Second", "https://chatgpt.com/c/SAVE-boundary");

            var firstId = await database.SaveMonitorAsync(first);
            var secondId = await database.SaveMonitorAsync(second);

            Assert.Equal(firstId, secondId);
            Assert.Equal(firstId, first.Id);
            Assert.Equal(firstId, second.Id);
            Assert.Single(await database.GetSavedMonitorsAsync());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExistingMonitorUpdateKeepsSameRowAndStillUpdatesConfiguration()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var monitor = NewMonitor("tab-a", "Original", "https://chatgpt.com/c/existing-update");
            var registration = await database.RegisterMonitorIfConversationAvailableAsync(monitor);
            Assert.True(registration.Created);

            monitor.Title = "Updated";
            monitor.AutoReply = "updated reply";
            monitor.ReplyDelaySeconds = 17;
            monitor.RotationCount = 9;
            var updatedId = await database.SaveMonitorAsync(monitor);

            Assert.Equal(registration.MonitorId, updatedId);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal(registration.MonitorId, saved.Id);
            Assert.Equal("Updated", saved.Title);
            Assert.Equal("updated reply", saved.AutoReply);
            Assert.Equal(17, saved.ReplyDelaySeconds);
            Assert.Equal(9, saved.RotationCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static SavedMonitor NewMonitor(string tabId, string title, string url)
        => new()
        {
            TabId = tabId,
            Title = title,
            Url = url,
            AutoReply = "continue",
            ReplyDelaySeconds = 3,
            TimerSeconds = 1,
            Enabled = true,
            ConversationRotationEnabled = true,
            NewChatStartMessage = "resume",
            NewChatDelaySeconds = 30,
            RotationCooldownSeconds = 60,
            MaxConversationRotations = 0,
            RotationCount = 0,
            ModelRoutingEnabled = false,
            PreferredModel = "Auto",
            FallbackModel = "Auto"
        };

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