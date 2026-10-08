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
    public void MonitorRunnerNeverInvokesManagedChromeKillRelaunchRecovery()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");

        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService.RestartAfterDelayAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("KillAllManagedChromeProcessesAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveRuntimeEvaluateTimeoutExhaustionUsesSameSessionRecovery()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var start = runner.IndexOf("private async Task<ChatPageState> ReadPassiveStateResilientAsync", StringComparison.Ordinal);
        var end = runner.IndexOf("private static Task<ChatPageState> InvokePassiveStateReaderAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var passiveRead = runner[start..end];

        Assert.Contains("const int maxAttempts = 4", passiveRead, StringComparison.Ordinal);
        Assert.Contains("WaitForSameManagedSessionAsync", passiveRead, StringComparison.Ordinal);
        Assert.Contains("passive same-session recovery", passiveRead, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no Chrome process was restarted", passiveRead, StringComparison.Ordinal);
        Assert.Contains("throw new ConversationTargetException", passiveRead, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService", passiveRead, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveReadRecoversKnownTargetLifecycleFailuresInsteadOfBlockingMonitor()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var start = runner.IndexOf("private async Task<ChatPageState> ReadPassiveStateResilientAsync", StringComparison.Ordinal);
        var end = runner.IndexOf("private static Task<ChatPageState> InvokePassiveStateReaderAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var passiveRead = runner[start..end];

        Assert.Contains("ChromeTransportFailureClassifier.IsTransient(ex)", passiveRead, StringComparison.Ordinal);
        Assert.Contains("RefreshLiveTabAsync(tab", passiveRead, StringComparison.Ordinal);
        Assert.Contains("Transient CDP target/context change", passiveRead, StringComparison.Ordinal);
        Assert.Contains("RecoveringCdpTarget", passiveRead, StringComparison.Ordinal);
        Assert.Contains("throw new ConversationTargetException", passiveRead, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus($\"BLOCKED", passiveRead, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService", passiveRead, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshTargetEndpointLossWaitsForSameSessionBeforeFifteenMinuteFallback()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var start = runner.IndexOf("private async Task<ChromeTab> CreateFreshTargetAsync", StringComparison.Ordinal);
        var end = runner.IndexOf("private async Task<ChromeTab> RollOverBeforeSendAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var freshTarget = runner[start..end];

        Assert.Contains("IsAutomationSessionAvailableAsync", freshTarget, StringComparison.Ordinal);
        Assert.Contains("WaitForSameManagedSessionAsync", freshTarget, StringComparison.Ordinal);
        Assert.Contains("RecoverAfterAuthorizedStartAsync", freshTarget, StringComparison.Ordinal);
        Assert.Contains("no browser will be closed or relaunched", freshTarget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CleanFreshTargetRecoveryDelay", freshTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService", freshTarget, StringComparison.Ordinal);

        var endpointProbe = freshTarget.IndexOf("IsAutomationSessionAvailableAsync", StringComparison.Ordinal);
        var sameSessionRecovery = freshTarget.IndexOf("WaitForSameManagedSessionAsync", StringComparison.Ordinal);
        var fifteenMinuteFallback = freshTarget.IndexOf("CleanFreshTargetRecoveryDelay", StringComparison.Ordinal);
        var destructiveCleanRecovery = freshTarget.IndexOf("RecoverAfterAuthorizedStartAsync", sameSessionRecovery, StringComparison.Ordinal);

        Assert.True(sameSessionRecovery > endpointProbe, "Non-destructive same-session recovery must follow a failed endpoint probe.");
        Assert.True(fifteenMinuteFallback > sameSessionRecovery, "The 15-minute wait is fallback only after bounded non-destructive same-session recovery.");
        Assert.True(destructiveCleanRecovery > fifteenMinuteFallback, "Tab-cleaning recovery must be reserved for the explicit delayed clean-retry path.");
    }

    [Fact]
    public void PhysicalSendPathRemainsFailClosedAndCannotInvokeBrowserRecovery()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var sendStart = runner.IndexOf("VerifiedDeliveryOutcome delivery;", StringComparison.Ordinal);
        var sendEnd = runner.IndexOf("catch (ConversationTargetException ex)", sendStart, StringComparison.Ordinal);

        Assert.True(sendStart >= 0);
        Assert.True(sendEnd > sendStart);
        var physicalSend = runner[sendStart..sendEnd];

        Assert.Contains("SendChatMessageWithOutcomeAsync", physicalSend, StringComparison.Ordinal);
        Assert.Contains("physical send outcome is uncertain", physicalSend, StringComparison.Ordinal);
        Assert.Contains("Automatic New Chat/resend is blocked", physicalSend, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEvaluateTimeoutRecoveryService", physicalSend, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoverAfterAuthorizedStartAsync", physicalSend, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileSessionDocumentsStickyRuntimeBrowserIdentity()
    {
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.Contains("Runtime recovery also remains passive once this selected", session, StringComparison.Ordinal);
        Assert.Contains("Runtime recovery can never reach Process.Start after a successful attachment.", session, StringComparison.Ordinal);
        Assert.Contains("Runtime Chrome auto-launch is disabled.", session, StringComparison.Ordinal);
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
