using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorConversationIdentityRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWorkspaceFiltersOperatorTabsAndDefensivelyGuardsAddAndStart()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("var chromePages = await _chrome.GetTabsAsync()", source, StringComparison.Ordinal);
        Assert.Contains("Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("Skipped non-conversation tab", source, StringComparison.Ordinal);
        Assert.Contains("saved URL is not a valid ChatGPT conversation", source, StringComparison.Ordinal);
        Assert.Contains("Only stable ChatGPT conversation URLs are shown here", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericChromeEnumerationRemainsUnfilteredForBrowserAndRecoveryOperations()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var getTabsStart = source.IndexOf(
            "public async Task<List<ChromeTab>> GetTabsAsync",
            StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf(
            "public Process LaunchMonitorChrome",
            getTabsStart,
            StringComparison.Ordinal);

        Assert.True(getTabsStart >= 0 && nextMethodStart > getTabsStart);
        var getTabs = source[getTabsStart..nextMethodStart];

        Assert.Contains("tabs.Add(new ChromeTab", getTabs, StringComparison.Ordinal);
        Assert.DoesNotContain("IsChatGptConversationUrl", getTabs, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationImportUsesSameConversationIdentityGuard()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupImportService.cs");

        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl(url)", source, StringComparison.Ordinal);
        Assert.Contains("stable /c/{{conversation-id}} identity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeHealthPresentation.IsChatGptTabUrl(url)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://chatgpt.com/")]
    [InlineData("https://chatgpt.com/share/public-id")]
    [InlineData("https://chatgpt.com/c/")]
    [InlineData("http://chatgpt.com/c/insecure-id")]
    public void BackupPlanRejectsChatGptPagesWithoutStableConversationIdentity(string invalidUrl)
    {
        var document = new ConfigurationBackupDocument(
            ConfigurationBackupService.SchemaVersion,
            DateTimeOffset.UtcNow,
            "1.8.0",
            ConfigurationBackupService.SensitivityNotice,
            Array.Empty<ConfigurationBackupSetting>(),
            new[]
            {
                new ConfigurationBackupMonitor(
                    "Invalid monitor",
                    invalidUrl,
                    "كمل",
                    3,
                    1,
                    true,
                    true,
                    "كمل",
                    30,
                    60,
                    0,
                    false,
                    "Auto",
                    "Auto")
            },
            ConfigurationBackupService.Exclusions);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ConfigurationBackupImportService.CreatePlan("invalid.json", document));

        Assert.Contains("stable /c/{conversation-id} identity", exception.Message, StringComparison.Ordinal);
    }
}