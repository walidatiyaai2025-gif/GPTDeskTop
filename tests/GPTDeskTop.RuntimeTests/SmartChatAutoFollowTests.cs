using GPTDeskTop.Configuration;

namespace GPTDeskTop.RuntimeTests;

public sealed class SmartChatAutoFollowTests
{
    private static string RepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SmartAutoFollowDefaultsAreSafeAndEnabled()
    {
        var config = new ChromeConfig();
        Assert.True(config.SmartAutoFollowEnabled);
        Assert.InRange(config.SmartAutoFollowThrottleMilliseconds, 150, 2000);
        Assert.InRange(config.SmartAutoFollowNearBottomPixels, 64, 600);
    }

    [Fact]
    public void ChatStateCacheContainsSmartFollowPauseResumeAndThrottleContract()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("const version = 5;", source, StringComparison.Ordinal);
        Assert.Contains("createSmartFollowController", source, StringComparison.Ordinal);
        Assert.Contains("paused-by-user", source, StringComparison.Ordinal);
        Assert.Contains("user-away-from-bottom", source, StringComparison.Ordinal);
        Assert.Contains("manual-near-bottom", source, StringComparison.Ordinal);
        Assert.Contains("smartFollowThrottleMs", source, StringComparison.Ordinal);
        Assert.Contains("smartFollowNearBottomPx", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('wheel'", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('touchmove'", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('keydown'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationSendRearmsFollowWithoutChangingDeliveryReceiptLogic()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var click = source.IndexOf("sendButton.click();", StringComparison.Ordinal);
        var rearm = source.IndexOf("autoFollow?.rearm?.('automation-send')", click, StringComparison.Ordinal);
        var submitted = source.IndexOf("var submitted = await EvaluateAsync", rearm, StringComparison.Ordinal);
        Assert.True(click >= 0);
        Assert.True(rearm > click);
        Assert.True(submitted > rearm);
        Assert.Contains("VerifiedSendDiagnostics.Record", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFollowIsUiOnlyAndPrivacySafe()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("RuntimeFlightRecorder.Record(\"AutoFollow\", \"StateChanged\", mode, eventName)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"AutoFollow\"", source.Replace("RuntimeFlightRecorder.Record(\"AutoFollow\", \"StateChanged\", mode, eventName)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("document.body.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document.cookie", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagedSettingsEnableSmartFollowByDefault()
    {
        var source = RepoFile("src", "GPTDeskTop", "appsettings.json");
        Assert.Contains("\"SmartAutoFollowEnabled\": true", source, StringComparison.Ordinal);
        Assert.Contains("\"SmartAutoFollowThrottleMilliseconds\": 400", source, StringComparison.Ordinal);
        Assert.Contains("\"SmartAutoFollowNearBottomPixels\": 180", source, StringComparison.Ordinal);
    }
}
