namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorConnectedChromeReuseRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MonitorStartDoesNotForceANewChromeWindow()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.DoesNotContain("\"--new-window\"", source, StringComparison.Ordinal);
        Assert.Contains("if (await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false)) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (IsLaunchedProcessAlive())", source, StringComparison.Ordinal);
        Assert.Contains("No second Chrome was opened", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnceSelectedEndpointWasObservedRuntimeCannotAutoLaunchAnotherChrome()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");
        var ensureStart = source.IndexOf("private async Task EnsureStartAuthorizedBrowserAvailableAsync", StringComparison.Ordinal);
        var launchStart = source.IndexOf("private async Task LaunchChromeForMonitorStartAsync", StringComparison.Ordinal);

        Assert.True(ensureStart >= 0);
        Assert.True(launchStart > ensureStart);
        var ensureRegion = source[ensureStart..launchStart];

        Assert.Contains("if (_lastEndpointSeenUtc is not null)", ensureRegion, StringComparison.Ordinal);
        Assert.Contains("runtime Chrome auto-launch is disabled", ensureRegion, StringComparison.Ordinal);
        Assert.Contains("No new Chrome was opened", ensureRegion, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", ensureRegion, StringComparison.Ordinal);

        var observedGuard = ensureRegion.IndexOf("if (_lastEndpointSeenUtc is not null)", StringComparison.Ordinal);
        var processLaunchCall = ensureRegion.IndexOf("await LaunchChromeForMonitorStartAsync", StringComparison.Ordinal);
        Assert.True(observedGuard >= 0 && processLaunchCall > observedGuard);
        var observedEndpointRegion = ensureRegion[observedGuard..processLaunchCall];
        Assert.Contains("throw new TimeoutException", observedEndpointRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveMonitorProcessIsNeverKilledToRecoverATransientCdpMiss()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.DoesNotContain("_launchedProcess.Kill", source, StringComparison.Ordinal);
        Assert.Contains("WaitForExistingEndpointAsync(TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
        Assert.Contains("_launchGate.WaitAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeRecoveryHasNoDirectProcessStartPrimitive()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");
        var recoveryStart = source.IndexOf("public async Task RecoverAfterAuthorizedStartAsync", StringComparison.Ordinal);
        var closeStart = source.IndexOf("public async Task CloseAutomationOwnedChatTabsAsync", StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0);
        Assert.True(closeStart > recoveryStart);
        var recoveryRegion = source[recoveryStart..closeStart];
        Assert.DoesNotContain("Process.Start", recoveryRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchChromeForMonitorStartAsync", recoveryRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveConnectionProbeRemainsLaunchFreeAndCanReportReality()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");
        var passiveStart = source.IndexOf("public Task<bool> TryEnsureConnectedAsync", StringComparison.Ordinal);
        var launchStart = source.IndexOf("private async Task LaunchChromeForMonitorStartAsync", StringComparison.Ordinal);

        Assert.True(passiveStart >= 0);
        Assert.True(launchStart > passiveStart);
        var passiveRegion = source[passiveStart..launchStart];
        Assert.DoesNotContain("Process.Start", passiveRegion, StringComparison.Ordinal);
        Assert.Contains("=> CanReadEndpointAsync(cancellationToken);", passiveRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshChatStillUsesExistingCdpTargetInsteadOfLaunchingChrome()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");
        var freshStart = source.IndexOf("public async Task<ChromeTab> CreateFreshConversationTabAsync", StringComparison.Ordinal);
        var recoveryStart = source.IndexOf("public async Task RecoverAfterAuthorizedStartAsync", StringComparison.Ordinal);

        Assert.True(freshStart >= 0);
        Assert.True(recoveryStart > freshStart);
        var freshRegion = source[freshStart..recoveryStart];
        Assert.Contains("Chrome.CreateNewChatTabAsync", freshRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", freshRegion, StringComparison.Ordinal);
    }
}