namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeProcessLifecycleRegressionTests
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
    public void LaunchMonitorChromeReusesTheTrackedLiveProcess()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var start = source.IndexOf("public Process LaunchMonitorChrome", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<ChromeTab> CreateTabAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var method = source[start..end];
        var guard = method.IndexOf("if (IsProcessRunning(_monitorChromeProcess)) return _monitorChromeProcess!;", StringComparison.Ordinal);
        var processStart = method.IndexOf("Process.Start", StringComparison.Ordinal);

        Assert.True(guard >= 0, "Launch must reuse the already tracked live monitor Chrome process.");
        Assert.True(processStart > guard, "The duplicate-process guard must execute before Process.Start.");
    }

    [Fact]
    public void CloseAllMonitorTabsTerminatesTheDedicatedBrowserAndTrackedProcessTree()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var start = source.IndexOf("public async Task CloseAllMonitorTabsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<bool> TrySelectModelAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var shutdown = source[start..end];

        Assert.Contains("TryGetBrowserTargetAsync", shutdown, StringComparison.Ordinal);
        Assert.Contains("\"Browser.close\"", shutdown, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", shutdown, StringComparison.Ordinal);
        Assert.Contains("_monitorChromeProcess = null", shutdown, StringComparison.Ordinal);
        Assert.Contains("_lastKnownWindowHandle = IntPtr.Zero", shutdown, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Clear()", shutdown, StringComparison.Ordinal);

        Assert.Contains("/json/version", source, StringComparison.Ordinal);
        Assert.Contains("webSocketDebuggerUrl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalShutdownStillInvokesMonitorBrowserCleanup()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var start = source.IndexOf("private async Task CompleteShutdownAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("protected override void Dispose", start, StringComparison.Ordinal);
        if (end < 0) end = source.Length;

        Assert.True(start >= 0);
        var shutdown = source[start..end];
        Assert.Contains("CloseAllMonitorTabsAsync", shutdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ChromeRecoveryTerminatesThePreviousMonitorBrowserBeforeRelaunch()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeRecoveryService.cs");
        var close = source.IndexOf("await _chrome.CloseAllMonitorTabsAsync", StringComparison.Ordinal);
        var launch = source.IndexOf("_chrome.LaunchMonitorChrome", StringComparison.Ordinal);

        Assert.True(close >= 0);
        Assert.True(launch > close, "Recovery must terminate the old monitor browser before launching its replacement.");
    }

    [Fact]
    public void CommittedInstanceHandoffStillPreservesTheMonitorBrowserForReplacementRuntime()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var start = source.IndexOf("private static async Task CompleteCommittedInstanceHandoffAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task FinalizeGracefulShutdownAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var handoff = source[start..end];

        Assert.DoesNotContain("CloseAllMonitorTabsAsync", handoff, StringComparison.Ordinal);
        Assert.Contains("leave those tabs", handoff, StringComparison.Ordinal);
        Assert.Contains("Environment.Exit(0)", handoff, StringComparison.Ordinal);
    }
}
