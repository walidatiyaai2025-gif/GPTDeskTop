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
    public void LiveMonitorProcessIsNeverKilledToRecoverATransientCdpMiss()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.DoesNotContain("_launchedProcess.Kill", source, StringComparison.Ordinal);
        Assert.Contains("WaitForExistingEndpointAsync(TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
        Assert.Contains("_launchGate.WaitAsync", source, StringComparison.Ordinal);
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
