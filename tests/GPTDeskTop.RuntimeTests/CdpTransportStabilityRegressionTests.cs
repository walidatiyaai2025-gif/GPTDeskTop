namespace GPTDeskTop.RuntimeTests;

public sealed class CdpTransportStabilityRegressionTests
{
    [Fact]
    public void StableTransportRecoveryRebindsSameConversationWithoutReloading()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome,
            "public async Task<bool> EnsureStableConversationTransportAsync",
            "private async Task TryRefreshTabBindingAsync");

        Assert.Contains("stableReadsRequired = Math.Clamp", method, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.FindBestBinding", method, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(originalUrl, current.Url)", method, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Invalidate(tab.Id)", method, StringComparison.Ordinal);
        Assert.Contains("stableReads >= stableReadsRequired", method, StringComparison.Ordinal);
        Assert.Contains("StableBindingCompleted", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalSubmitRequiresStableTransportBeforeAndAfterComposerPreparation()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome,
            "public async Task<bool> SendChatMessageAsync",
            "public async Task<bool> SendChatMessageVerifiedAsync");

        Assert.True(Count(method, "EnsureStableConversationTransportAsync") >= 3);
        Assert.Contains("cdp-transport-not-stable-before-composer", method, StringComparison.Ordinal);
        Assert.Contains("cdp-transport-not-stable-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("composer-revalidation-required-after-cdp-rebind", method, StringComparison.Ordinal);
        Assert.Contains("pre-submit-cdp-recovered-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("TryDispatchNativeSendClickAsync", method, StringComparison.Ordinal);

        var physicalBoundary = method.IndexOf("var submitted = await TryDispatchNativeSendClickAsync", StringComparison.Ordinal);
        var preSubmitCatch = method.IndexOf("pre-submit-cdp-recovered-before-physical-input", StringComparison.Ordinal);
        Assert.True(physicalBoundary > preSubmitCatch, "Recoverable pre-submit transport failures must be handled before native physical input.");
    }

    [Fact]
    public void MonitorTransportRetryActivelyRebindsAndClearsDegradedPresentationOnlyAfterRecovery()
    {
        var monitor = MonitorSource();
        var method = Slice(monitor,
            "private async Task<ChatPageState> GetChatStateWithRetryAsync",
            "private static bool IsTransientChromeException");

        Assert.Contains("Chrome/CDP transport disconnect retry", method, StringComparison.Ordinal);
        Assert.Contains("EnsureStableConversationTransportAsync", method, StringComparison.Ordinal);
        Assert.Contains("Chrome/CDP recovery complete", method, StringComparison.Ordinal);
        Assert.Contains("attempt = 0", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentityIsV206()
    {
        var root = Root();
        Assert.Contains("<Version>2.0.6</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "GPTDeskTop.csproj")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.6</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj")), StringComparison.Ordinal);
        Assert.Contains("internal const string Version = \"2.0.6\";", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "Program.cs")), StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
    private static string MonitorSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
