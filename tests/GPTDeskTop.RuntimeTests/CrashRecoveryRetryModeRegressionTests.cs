namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryRetryModeRegressionTests
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
    public void ProgramPropagatesCurrentStartupCrashStateIntoRecoveryMode()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("var currentStartupWasUnclean = CrashRecoveryStateService.PrepareStartupAsync(database)", source, StringComparison.Ordinal);
        Assert.Contains("? CrashRecoveryMode.FreshCrashReset", source, StringComparison.Ordinal);
        Assert.Contains(": CrashRecoveryMode.PendingRetry", source, StringComparison.Ordinal);
        Assert.Contains("CrashRecoveryService.RecoverIfPendingAsync(", source, StringComparison.Ordinal);
        Assert.Contains("recoveryMode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingRetryPathDoesNotContainGlobalTeardownCalls()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryService.cs");
        var freshStart = source.IndexOf("if (mode == CrashRecoveryMode.FreshCrashReset)", StringComparison.Ordinal);
        var retryStart = source.IndexOf("else if (firstValidMonitor is not null)", freshStart, StringComparison.Ordinal);
        var loopStart = source.IndexOf("var outcomes = new List<CrashRecoveryOutcome>", retryStart, StringComparison.Ordinal);

        Assert.True(freshStart >= 0);
        Assert.True(retryStart > freshStart);
        Assert.True(loopStart > retryStart);

        var freshBlock = source[freshStart..retryStart];
        var retryBlock = source[retryStart..loopStart];

        Assert.Contains("StopAllMonitorsAsync", freshBlock, StringComparison.Ordinal);
        Assert.Contains("CloseAllMonitorTabsAsync", freshBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("StopAllMonitorsAsync", retryBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAllMonitorTabsAsync", retryBlock, StringComparison.Ordinal);
        Assert.Contains("GetTabsWithChromeStartupRecoveryAsync", retryBlock, StringComparison.Ordinal);
        Assert.Contains("launchOnFirstTransient: true", retryBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryGateSerializesBeforePendingStateIsRead()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "CrashRecoveryService.cs");
        var gateDeclaration = source.IndexOf("private static readonly SemaphoreSlim RecoveryGate", StringComparison.Ordinal);
        var gateWait = source.IndexOf("await RecoveryGate.WaitAsync(cancellationToken)", StringComparison.Ordinal);
        var coreCall = source.IndexOf("await RecoverCoreAsync(runtime, database, mode, cancellationToken)", gateWait, StringComparison.Ordinal);
        var release = source.IndexOf("RecoveryGate.Release()", coreCall, StringComparison.Ordinal);
        var coreMethod = source.IndexOf("private static async Task RecoverCoreAsync", release, StringComparison.Ordinal);
        var pendingRead = source.IndexOf("GetSettingAsync(\"CrashRecoveryPending\"", coreMethod, StringComparison.Ordinal);

        Assert.True(gateDeclaration >= 0);
        Assert.True(gateWait > gateDeclaration);
        Assert.True(coreCall > gateWait);
        Assert.True(release > coreCall);
        Assert.True(coreMethod > release);
        Assert.True(pendingRead > coreMethod);
    }
}