using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorModeRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("https://chatgpt.com/c/abc123", "abc123")]
    [InlineData("https://chatgpt.com/c/abc123?model=auto", "abc123")]
    [InlineData("https://CHATGPT.com/c/abc123/", "abc123")]
    public void StableConversationIdentityIsRequired(string url, string expected)
    {
        Assert.True(SimpleMonitorProfileSession.TryGetConversationId(url, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(SimpleMonitorProfileSession.TryGetConversationId("https://chatgpt.com/", out _));
        Assert.False(SimpleMonitorProfileSession.TryGetConversationId("https://example.com/c/abc123", out _));
    }

    [Fact]
    public void AlternateFormExposesOnlyTheRequestedSimpleMonitorBusiness()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");

        Assert.Contains("Monitor Only — Same Chat", source, StringComparison.Ordinal);
        Assert.Contains("Chrome profile", source, StringComparison.Ordinal);
        Assert.Contains("Same Chat = ON", source, StringComparison.Ordinal);
        Assert.Contains("New Chat = OFF", source, StringComparison.Ordinal);
        Assert.Contains("Rotation = OFF", source, StringComparison.Ordinal);
        Assert.Contains("Minimum = 15", source, StringComparison.Ordinal);
        Assert.Contains("Stored message sequence", source, StringComparison.Ordinal);
        Assert.Contains("Load JSON Plan", source, StringComparison.Ordinal);
        Assert.Contains("Download Sample JSON", source, StringComparison.Ordinal);
        Assert.Contains("Copy ChatGPT Prompt", source, StringComparison.Ordinal);
        Assert.Contains("Preview / Validate", source, StringComparison.Ordinal);
        Assert.Contains("Runtime Inspector", source, StringComparison.Ordinal);
        Assert.Contains("DrawMode = DrawMode.OwnerDrawFixed", source, StringComparison.Ordinal);
        Assert.Contains("CheckpointPlanMessageSentAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (!loop)", runner, StringComparison.Ordinal);
        Assert.Contains("messageIndex = 0", runner, StringComparison.Ordinal);
        Assert.Contains("messageIndex++", runner, StringComparison.Ordinal);
        Assert.Contains("Runtime.evaluate timeout", runner, StringComparison.Ordinal);
        Assert.Contains("Step.EffectiveDelaySeconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeNeverCreatesOrRotatesToAnotherChat()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");

        Assert.DoesNotContain("CreateNewChatTabAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversationRotation", runner, StringComparison.Ordinal);
        Assert.Contains("openIfMissing: false", runner, StringComparison.Ordinal);
        Assert.Contains("No other chat will be used", runner, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", runner, StringComparison.Ordinal);
        Assert.Contains("ReadChatStateCoreAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("GetChatStateAsync(", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeSwitchStopsClassicSavedMonitorsBeforeHidingCurrentUi()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorModeBootstrap.cs");

        Assert.Contains("await monitor.StopAllAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceDesiredMonitorIdsAsync(database, Array.Empty<long>())", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("await monitor.StopAllAsync()", StringComparison.Ordinal)
            < source.IndexOf("main.Hide()", StringComparison.Ordinal));
        Assert.Contains("Current GPTDeskTop", source, StringComparison.Ordinal);
        Assert.Contains("Monitor Only — Same Chat", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedChromeProfileGetsASeparatePersistentAutomationSafeSession()
    {
        var catalog = ReadSource("src", "GPTDeskTop", "Services", "ChromeProfileCatalog.cs");
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.Contains("Local State", catalog, StringComparison.Ordinal);
        Assert.Contains("info_cache", catalog, StringComparison.Ordinal);
        Assert.Contains("ChromeProfiles", catalog, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-port=", session, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", session, StringComparison.Ordinal);
        Assert.Contains("Profile.ManagedUserDataDirectory", session, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductVersionIsBumpedToTwoPointZeroPointNineteen()
    {
        var props = ReadSource("Directory.Build.props");
        Assert.Contains("<GPTDeskTopVersion>2.0.19</GPTDeskTopVersion>", props, StringComparison.Ordinal);
    }
}
