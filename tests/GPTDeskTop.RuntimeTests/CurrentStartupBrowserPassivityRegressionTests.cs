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
    public void MonitorOnlyStartupReplacesCurrentUiAndRemainsPassiveUntilExplicitStart()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var gate = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");

        Assert.Contains("MonitorOnlyStartupGate.Run(database);", program, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop-MonitorOnly-SingleInstance", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Run(mainForm);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainForm(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashRecoveryService.RecoverIfPendingAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffCoordinator.ResumeRunningMonitorsAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("LastWorkingStateService.ResumeDesiredMonitorsAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchMonitorChrome", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTabAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageVerifiedAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitorAsync", program, StringComparison.Ordinal);
        Assert.Contains("using var form = new SimpleMonitorForm(database);", gate, StringComparison.Ordinal);
        Assert.Contains("Application.Run(form);", gate, StringComparison.Ordinal);
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
