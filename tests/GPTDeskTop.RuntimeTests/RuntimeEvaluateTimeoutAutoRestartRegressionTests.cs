namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeEvaluateTimeoutAutoRestartRegressionTests
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
    public void RuntimeEvaluateRecoveryWaitsExactlyOneMinuteBeforeRestart()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeEvaluateTimeoutRecoveryService.cs");

        Assert.Contains("RestartDelay = TimeSpan.FromMinutes(1)", source, StringComparison.Ordinal);
        var wait = source.IndexOf("await Task.Delay(RestartDelay", StringComparison.Ordinal);
        var kill = source.IndexOf("await KillAllManagedChromeProcessesAsync", StringComparison.Ordinal);
        var launch = source.IndexOf("Process.Start", StringComparison.Ordinal);

        Assert.True(wait >= 0);
        Assert.True(kill > wait, "Managed Chrome must not be killed before the one-minute recovery delay completes.");
        Assert.True(launch > kill, "Replacement Chrome must start only after managed-process cleanup completes.");
    }

    [Fact]
    public void RecoveryRequiresExplicitStartMonitorAuthorizationAndNeverUsesGlobalChromeKill()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeEvaluateTimeoutRecoveryService.cs");

        Assert.Contains("MonitorOnlyManagedChromeGuard.HasExplicitStartAuthorization", source, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", source, StringComparison.Ordinal);
        Assert.Contains("ChromeProfileCatalog.Discover()", source, StringComparison.Ordinal);
        Assert.Contains("CommandLineReferencesUserDataDirectory", source, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.GetProcesses()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveRuntimeEvaluateTimeoutExhaustionRestartsAndResumesInsteadOfStoppingMonitor()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var start = runner.IndexOf("private async Task<ChatPageState> ReadPassiveStateResilientAsync", StringComparison.Ordinal);
        var end = runner.IndexOf("private static Task<ChatPageState> InvokePassiveStateReaderAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var passiveRead = runner[start..end];

        Assert.Contains("const int maxAttempts = 4", passiveRead, StringComparison.Ordinal);
        Assert.Contains("RuntimeEvaluateTimeoutRecoveryService.RestartAfterDelayAsync", passiveRead, StringComparison.Ordinal);
        Assert.Contains("WaitingRuntimeEvaluateRestart", passiveRead, StringComparison.Ordinal);
        Assert.Contains("existing pending/checkpoint state", passiveRead, StringComparison.Ordinal);
        Assert.Contains("throw new ConversationTargetException", passiveRead, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalSendPathRemainsFailClosedAndCannotInvokeManagedRestart()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var sendStart = runner.IndexOf("bool sent;", StringComparison.Ordinal);
        var sendEnd = runner.IndexOf("catch (ConversationTargetException ex)", sendStart, StringComparison.Ordinal);

        Assert.True(sendStart >= 0);
        Assert.True(sendEnd > sendStart);
        var physicalSend = runner[sendStart..sendEnd];

        Assert.Contains("SendChatMessageVerifiedAsync", physicalSend, StringComparison.Ordinal);
        Assert.Contains("physical send outcome is uncertain", physicalSend, StringComparison.Ordinal);
        Assert.Contains("automatic New Chat/resend is blocked", physicalSend, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService", physicalSend, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshTargetPassiveReadUsesTheSameRuntimeEvaluateRecoveryBoundary()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var start = runner.IndexOf("private async Task<ChromeTab> CreateFreshTargetAsync", StringComparison.Ordinal);
        var end = runner.IndexOf("private async Task<ChromeTab> RollOverBeforeSendAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var freshTarget = runner[start..end];

        Assert.Contains("ReadPassiveStateResilientAsync(session, live", freshTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokePassiveStateReaderAsync(session.Chrome, live", freshTarget, StringComparison.Ordinal);
    }
}
