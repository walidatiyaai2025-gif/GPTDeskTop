using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class FieldInvalidConversationIdentityRegressionTests
{
    private const string PseudoWebIdentity = "https://chatgpt.com/c/WEB:5f4000c3-608f-4aba-8b2d-152edbf5e000";
    private const string ActualOpenConversation = "https://chatgpt.com/c/6a809c70-42e0-83ed-a20f-6ba37a61ba1c";

    [Theory]
    [InlineData(PseudoWebIdentity)]
    [InlineData("https://chatgpt.com/c/WEB%3A5f4000c3-608f-4aba-8b2d-152edbf5e000")]
    [InlineData("https://chatgpt.com/c/tab:7482a6ca173dbdbc")]
    [InlineData("https://chatgpt.com/c/conv%3A8a5edab282632443")]
    public void RuntimeLocatorSegmentsAreNotStableConversationIdentities(string url)
        => Assert.False(RuntimeHealthPresentation.IsChatGptConversationUrl(url));

    [Theory]
    [InlineData(ActualOpenConversation)]
    [InlineData("https://chatgpt.com/c/runtime-boundary-valid")]
    [InlineData("https://chat.openai.com/c/legacy-stable-conversation")]
    public void OrdinaryStableConversationIdentitiesRemainAccepted(string url)
        => Assert.True(RuntimeHealthPresentation.IsChatGptConversationUrl(url));

    [Fact]
    public void InvalidSavedIdentityNeverBindsToTheOnlyOpenConversation()
    {
        var monitor = new SavedMonitor
        {
            Id = 1,
            TabId = "DA45D011AD912DF0",
            Title = "ChatGPT",
            Url = PseudoWebIdentity,
            Enabled = true
        };
        var onlyOpenTab = new ChromeTab
        {
            Id = "DA45D011AD912DF0",
            Title = "معلومات عن مصر",
            Url = ActualOpenConversation,
            Type = "page"
        };

        var resolution = SavedMonitorTabResolver.Resolve(monitor, new[] { onlyOpenTab });

        Assert.False(resolution.Found);
        Assert.Null(resolution.Tab);
        Assert.Contains("not a stable ChatGPT conversation identity", resolution.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedMonitorHealthSurfacesRepairInsteadOfMissingTabForPseudoIdentity()
    {
        var monitor = new SavedMonitor
        {
            Id = 1,
            Url = PseudoWebIdentity,
            Enabled = true
        };

        var health = SavedMonitorHealthPresentation.Evaluate(
            monitor,
            workerRunning: false,
            duplicateOwnership: false,
            conversationTabAvailable: false,
            pageState: null);

        Assert.False(health.IsHealthy);
        Assert.Contains("Invalid", health.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Repair Monitor Conversation Ownership", health.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Conversation tab is not open", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PseudoIdentityBecomesEligibleForExplicitRepairWithoutChangingMonitorId()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "field-identity.db"));
            await database.InitializeAsync();
            var monitor = new SavedMonitor
            {
                TabId = "legacy-web-target",
                Title = "ChatGPT",
                Url = PseudoWebIdentity,
                AutoReply = "كمل",
                Enabled = true
            };
            var monitorId = await database.SaveMonitorAsync(monitor);
            var service = new MonitorIdentityRepairService(database);

            var result = await service.RebindAsync(
                monitorId,
                new ChromeTab
                {
                    Id = "actual-open-target",
                    Title = "معلومات عن مصر",
                    Url = ActualOpenConversation,
                    Type = "page"
                });

            Assert.Equal(monitorId, result.MonitorId);
            Assert.Equal(PseudoWebIdentity, result.PreviousUrl);
            Assert.Equal(ActualOpenConversation, result.NewUrl);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync());
            Assert.Equal(monitorId, saved.Id);
            Assert.Equal("actual-open-target", saved.TabId);
            Assert.Equal(ActualOpenConversation, saved.Url);
            Assert.Equal("كمل", saved.AutoReply);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void RepairUiDiscoversInvalidPseudoIdentityAndOffersOnlyStableUnownedTabs()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs"));

        Assert.Contains("!RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)", source, StringComparison.Ordinal);
        Assert.Contains("Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("Where(tab => !ownedConversationUrls.Contains(tab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("Rebind Monitor", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartPathFailsClosedBeforeMissingTabRecoveryForInvalidIdentity()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "MainForm.cs"));
        var methodStart = source.IndexOf("private async Task StartMonitorAsync", StringComparison.Ordinal);
        var invalidGate = source.IndexOf("!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url)", methodStart, StringComparison.Ordinal);
        var resolve = source.IndexOf("var tab = ResolveTab(monitor);", methodStart, StringComparison.Ordinal);
        var recovery = source.IndexOf("MonitorTabRecoveryService.EnsureMonitorTabAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(invalidGate > methodStart);
        Assert.True(resolve > invalidGate);
        Assert.True(recovery > resolve);
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
