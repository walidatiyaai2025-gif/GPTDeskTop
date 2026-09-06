namespace GPTDeskTop.RuntimeTests;

public sealed class CurrentStartupBrowserPassivityRegressionTests
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
    public void CurrentUiShownDoesNotAutoRecoverLaunchOrResumeBrowserWork()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var start = program.IndexOf("mainForm.Shown += async", StringComparison.Ordinal);
        var end = program.IndexOf("Application.Run(mainForm);", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Current GPTDeskTop Shown startup block was not found.");
        var shownBlock = program[start..end];

        Assert.Contains("Runtime.StartupBrowserMutationPolicy", shownBlock, StringComparison.Ordinal);
        Assert.Contains("OperatorOnly", shownBlock, StringComparison.Ordinal);
        Assert.Contains("Runtime.StartupAutoResumeDeferred", shownBlock, StringComparison.Ordinal);
        Assert.Contains("ReplaceDesiredMonitorIdsAsync", shownBlock, StringComparison.Ordinal);

        Assert.DoesNotContain("CrashRecoveryService.RecoverIfPendingAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffCoordinator.ResumeRunningMonitorsAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("LastWorkingStateService.ResumeDesiredMonitorsAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeIfActiveAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchMonitorChrome", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTabAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageVerifiedAsync", shownBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitorAsync", shownBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserMutationRemainsAvailableOnlyThroughExplicitOperatorActions()
    {
        var mainForm = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        var launchStart = mainForm.IndexOf("private async Task LaunchChromeAsync()", StringComparison.Ordinal);
        var launchEnd = mainForm.IndexOf("private async Task CreateNewChatMonitorAsync()", launchStart, StringComparison.Ordinal);
        Assert.True(launchStart >= 0 && launchEnd > launchStart, "Explicit Launch Chrome handler was not found.");
        Assert.Contains("_chrome.LaunchMonitorChrome();", mainForm[launchStart..launchEnd], StringComparison.Ordinal);

        var startMonitor = mainForm.IndexOf("private async Task StartMonitorAsync(", StringComparison.Ordinal);
        var resolveTab = mainForm.IndexOf("private ChromeTab? ResolveTab", startMonitor, StringComparison.Ordinal);
        Assert.True(startMonitor >= 0 && resolveTab > startMonitor, "Explicit Start Monitor handler was not found.");
        Assert.Contains("MonitorTabRecoveryService.EnsureMonitorTabAsync", mainForm[startMonitor..resolveTab], StringComparison.Ordinal);
    }
}
