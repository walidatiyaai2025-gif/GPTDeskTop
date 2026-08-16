using System.Reflection;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

// Locks down browser/tab recreation, restart continuation, and non-freezing shutdown behavior.
public sealed class MonitorRestartRecoveryRegressionTests
{
    [Fact]
    public void VerifiedSenderCanRequireANewRepeatedUserTurn()
    {
        var method = typeof(ChromeDevToolsService).GetMethod(
            "SendChatMessageVerifiedAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(ChromeTab), typeof(string), typeof(CancellationToken), typeof(bool)],
            modifiers: null);

        Assert.NotNull(method);
        var requireNewTurn = method!.GetParameters()[3];
        Assert.True(requireNewTurn.HasDefaultValue);
        Assert.Equal(false, requireNewTurn.DefaultValue);

        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains(
            "MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt",
            source,
            StringComparison.Ordinal);
        Assert.Contains("deliveryState.AssistantCount", source, StringComparison.Ordinal);
        Assert.Contains("deliveryState.IsGenerating", source, StringComparison.Ordinal);
        Assert.Contains(
            "current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MissingMonitorTabRecoveryReacquiresExactConversationWithoutDestructiveRestart()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");

        Assert.Contains("SavedMonitorTabResolver.Resolve(monitor, tabs)", source, StringComparison.Ordinal);
        Assert.Contains("if (tabs is not null)", source, StringComparison.Ordinal);
        Assert.Contains("chrome.CreateTabAsync(monitor.Url", source, StringComparison.Ordinal);
        Assert.Contains("chrome.LaunchMonitorChrome(monitor.Url)", source, StringComparison.Ordinal);
        Assert.Contains("Only a genuinely unavailable CDP endpoint is allowed to restart", source, StringComparison.Ordinal);
        Assert.Contains("WaitForChatReachableAsync(chrome, recoveredTab", source, StringComparison.Ordinal);
        Assert.Contains("PersistRuntimeTargetAsync(database, monitor, recoveredTab", source, StringComparison.Ordinal);
        Assert.Contains("MonitorTabRebound", source, StringComparison.Ordinal);
        Assert.Contains("monitor.AutoReply", source, StringComparison.Ordinal);
        Assert.Contains("chrome.SendChatMessageVerifiedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", source, StringComparison.Ordinal);
        Assert.Contains("RestartFollowUpSent", source, StringComparison.Ordinal);
        Assert.Contains("RestartFollowUpFailed", source, StringComparison.Ordinal);
        Assert.Contains("monitor.ModelRoutingEnabled", source, StringComparison.Ordinal);
        Assert.Contains("chrome.TrySelectModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("ChromeHidden", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForChatReadyAsync(chrome, recoveredTab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageAsync(tab, followUp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReacquisitionNeverSendsFollowUpWhileRecoveredConversationIsGenerating()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");

        Assert.Contains("if (!recoveredState.IsGenerating)", source, StringComparison.Ordinal);
        Assert.Contains("&& !recoveredState.IsGenerating", source, StringComparison.Ordinal);
        Assert.Contains("MonitorTabReboundGenerating", source, StringComparison.Ordinal);
        Assert.Contains("without sending a follow-up", source, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(recoveredState.ErrorText)", source, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupResumeUsesSameRecoveryPrimitiveForEveryDesiredMonitor()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");

        Assert.Contains("MonitorTabRecoveryService.EnsureMonitorTabAsync", source, StringComparison.Ordinal);
        Assert.Contains("sendFollowUpWhenRecreated: true", source, StringComparison.Ordinal);
        Assert.Contains("await monitorService.StartMonitorAsync(savedMonitor, recovery.Tab)", source, StringComparison.Ordinal);
        Assert.Contains("RecreatedTabAndFollowUpSent", source, StringComparison.Ordinal);
        Assert.Contains("RecreatedTabFollowUpFailed", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceDesiredMonitorIdsAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualAndStartAllPathsRecoverInsteadOfStoppingAtMatchingTabNotOpen()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var start = Slice(
            source,
            "private async Task StartMonitorAsync",
            "private ChromeTab? ResolveTab");

        Assert.Contains("matching tab not open. Reopening the saved conversation", start, StringComparison.Ordinal);
        Assert.Contains("MonitorTabRecoveryService.EnsureMonitorTabAsync", start, StringComparison.Ordinal);
        Assert.Contains("sendFollowUpWhenRecreated: true", start, StringComparison.Ordinal);
        Assert.Contains("tab = recovery.Tab", start, StringComparison.Ordinal);
        Assert.Contains("await _monitor.StartMonitorAsync(monitor, tab)", start, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AppendActivity($\"Monitor #{monitor.Id}: matching tab not open.\");",
            start,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShutdownShowsResponsiveProgressAndPersistsRunningSetBeforeWorkersStop()
    {
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var overlay = ReadSource("src", "GPTDeskTop", "UI", "ShutdownLoadingOverlay.cs");

        Assert.Contains("private readonly ShutdownLoadingOverlay _shutdownOverlay = new();", main, StringComparison.Ordinal);
        Assert.Contains("_shutdownOverlay.ShowStatus(\"Preparing a safe shutdown…\")", main, StringComparison.Ordinal);
        Assert.Contains("_shutdownOverlay.SetStatus(\"Stopping monitor workers safely…\")", main, StringComparison.Ordinal);
        Assert.Contains("_shutdownOverlay.SetStatus(\"Closing the monitor Chrome window and tabs…\")", main, StringComparison.Ordinal);
        Assert.Contains("ProgressBarStyle.Marquee", overlay, StringComparison.Ordinal);
        Assert.Contains("MarqueeAnimationSpeed", overlay, StringComparison.Ordinal);

        var close = Slice(main, "protected override void OnFormClosing", "private async Task CompleteShutdownAsync");
        Assert.DoesNotContain("Enabled = false;", close, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWaitCursor = true;", close, StringComparison.Ordinal);

        var shutdown = Slice(main, "private async Task CompleteShutdownAsync", "private async Task PersistRunningMonitorIntentAsync");
        var persistIndex = shutdown.IndexOf("PersistRunningMonitorIntentAsync(timeout.Token)", StringComparison.Ordinal);
        var stopIndex = shutdown.IndexOf("_monitor.StopAllAsync().WaitAsync(timeout.Token)", StringComparison.Ordinal);
        Assert.True(persistIndex >= 0 && stopIndex > persistIndex);

        Assert.Contains("LastWorkingStateService.ReplaceDesiredMonitorIdsAsync", main, StringComparison.Ordinal);
        Assert.Contains("_monitor.IsMonitorRunning(saved.Id)", main, StringComparison.Ordinal);
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
