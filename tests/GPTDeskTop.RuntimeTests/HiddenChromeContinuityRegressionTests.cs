namespace GPTDeskTop.RuntimeTests;

public sealed class HiddenChromeContinuityRegressionTests
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
    public void MonitorChromeLaunchDisablesHiddenWindowBackgroundThrottling()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("--disable-background-timer-throttling", source, StringComparison.Ordinal);
        Assert.Contains("--disable-backgrounding-occluded-windows", source, StringComparison.Ordinal);
        Assert.Contains("--disable-renderer-backgrounding", source, StringComparison.Ordinal);
        Assert.Contains("--disable-features=CalculateNativeWinOcclusion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HideUsesNativeWindowPathBeforeCdpMinimizeFallback()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var hideStart = source.IndexOf("public async Task<bool> HideMonitorChromeAsync", StringComparison.Ordinal);
        var showStart = source.IndexOf("public async Task<bool> ShowMonitorChromeAsync", StringComparison.Ordinal);
        Assert.True(hideStart >= 0 && showStart > hideStart);

        var hideMethod = source[hideStart..showStart];
        var resolveIndex = hideMethod.IndexOf("ResolveMonitorWindowHandleAsync", StringComparison.Ordinal);
        var nativeHideIndex = hideMethod.IndexOf("ShowWindow(handle, SwHide)", StringComparison.Ordinal);
        var cdpFallbackIndex = hideMethod.IndexOf("SetAllBrowserWindowsStateAsync(\"minimized\"", StringComparison.Ordinal);

        Assert.True(resolveIndex >= 0);
        Assert.True(nativeHideIndex > resolveIndex);
        Assert.True(cdpFallbackIndex > nativeHideIndex);
        Assert.Contains("_monitorChromeHidden = true", hideMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoRecoveryRequiresEndpointGraceBeforeBrowserTeardown()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var recoverStart = source.IndexOf("private async Task<bool> RecoverMonitorTabAsync", StringComparison.Ordinal);
        var tryGetStart = source.IndexOf("private async Task<List<ChromeTab>?> TryGetLiveTabsAsync", recoverStart, StringComparison.Ordinal);
        Assert.True(recoverStart >= 0 && tryGetStart > recoverStart);

        var recoverMethod = source[recoverStart..tryGetStart];
        var graceIndex = recoverMethod.IndexOf("WaitForLiveTabsAfterTransportFailureAsync", StringComparison.Ordinal);
        var closeIndex = recoverMethod.IndexOf("CloseAllMonitorTabsAsync", StringComparison.Ordinal);

        Assert.True(graceIndex >= 0);
        Assert.True(closeIndex > graceIndex);
        Assert.Contains("MonitorRecoveryEndpointGraceAttempts = 8", source, StringComparison.Ordinal);
        Assert.Contains("MonitorRecoveryEndpointGraceDelayMs = 250", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DestructiveRecoveryRestoresHiddenPreference()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("var restoreHidden = _monitorChromeHidden", source, StringComparison.Ordinal);
        Assert.Contains("if (restoreHidden)", source, StringComparison.Ordinal);
        Assert.Contains("await HideMonitorChromeAsync(cancellationToken);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedBrowserCloseSocketTeardownIsNotLoggedAsFailure()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("if (!IsExpectedBrowserCloseDisconnect(ex))", source, StringComparison.Ordinal);
        Assert.Contains("current is WebSocketException or ObjectDisposedException", source, StringComparison.Ordinal);
        Assert.Contains("ExceptionLogService.Log(ex, \"ChromeDevToolsService.CloseMonitorBrowser\")", source, StringComparison.Ordinal);
    }
}
