namespace GPTDeskTop.RuntimeTests;

public sealed class StartupSavedMonitorContinuationRegressionTests
{
    [Fact]
    public void ExistingSavedConversationGetsOneExplicitNewContinuationBeforeWorkerStarts()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");

        var recovery = source.IndexOf("MonitorTabRecoveryService.EnsureMonitorTabAsync", StringComparison.Ordinal);
        var existingBranch = source.IndexOf("if (!recovery.Recreated && !string.IsNullOrWhiteSpace(savedMonitor.AutoReply))", recovery, StringComparison.Ordinal);
        var startupSend = source.IndexOf("SendExistingTabStartupFollowUpAsync(", existingBranch, StringComparison.Ordinal);
        var startWorker = source.IndexOf("monitorService.StartMonitorAsync(savedMonitor, recovery.Tab)", startupSend, StringComparison.Ordinal);

        Assert.True(recovery >= 0);
        Assert.True(existingBranch > recovery);
        Assert.True(startupSend > existingBranch);
        Assert.True(startWorker > startupSend);
        Assert.Contains("recovery.FollowUpSent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRepeatedAutoReplyCannotReuseAnOldMatchingUserTailAsReceipt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        var helper = Slice(
            source,
            "private static async Task<bool> SendExistingTabStartupFollowUpAsync",
            "private static async Task PersistResumeDiagnosticsAsync");

        Assert.Contains("chrome.SendChatMessageVerifiedAsync(", helper, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageAsync(", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupSendDiagnosticsAreCorrelatedToTheCorrectMonitorAndConversation()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        var helper = Slice(
            source,
            "private static async Task<bool> SendExistingTabStartupFollowUpAsync",
            "private static async Task PersistResumeDiagnosticsAsync");

        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url)", helper, StringComparison.Ordinal);
        Assert.Contains("StartupResumeFollowUpSent", helper, StringComparison.Ordinal);
        Assert.Contains("StartupResumeFollowUpDeferred", helper, StringComparison.Ordinal);
        Assert.Contains("monitor.Id", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void DeferredOrUncertainStartupDeliveryStillStartsTheMonitorWorker()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        var startupSend = source.IndexOf("SendExistingTabStartupFollowUpAsync(", StringComparison.Ordinal);
        var startWorker = source.IndexOf("monitorService.StartMonitorAsync(savedMonitor, recovery.Tab)", startupSend, StringComparison.Ordinal);
        var runningCheck = source.IndexOf("monitorService.IsMonitorRunning(savedMonitor.Id)", startWorker, StringComparison.Ordinal);

        Assert.True(startupSend >= 0);
        Assert.True(startWorker > startupSend);
        Assert.True(runningCheck > startWorker);
        Assert.Contains("PersistedWorkingStateFollowUpDeferred", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("return false;", Slice(
            source,
            "private static async Task<bool> SendExistingTabStartupFollowUpAsync",
            "private static async Task PersistResumeDiagnosticsAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupStillResumesOnlyPersistedDesiredRunningMonitorsNotEverySavedRow()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");

        Assert.Contains("var requestedIds = await GetDesiredMonitorIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var monitorId in requestedIds)", source, StringComparison.Ordinal);
        Assert.Contains("resumable.Add(savedMonitor);", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceDesiredMonitorIdsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var savedMonitor in savedById.Values)", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
