using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorRestartRecoveryRegressionTests
{
    [Fact]
    public void RunningWorkerRecoveryInvokesSavedTabRecoveryAfterSustainedChromeFailure()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.Contains("attempting saved-conversation recovery", source, StringComparison.Ordinal);
        Assert.Contains("MonitorTabRecoveryService.EnsureMonitorTabAsync(", source, StringComparison.Ordinal);
        Assert.Contains("sendFollowUpWhenRecreated: true", source, StringComparison.Ordinal);
        Assert.Contains("tab = recovery.Tab;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await StartMonitorAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedSenderCanRequireANewRepeatedUserTurn()
    {
        var serviceType = typeof(ChromeDevToolsService);
        var verified = serviceType.GetMethod(
            "SendChatMessageVerifiedAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types:
            [
                typeof(ChromeTab),
                typeof(string),
                typeof(CancellationToken),
                typeof(bool)
            ],
            modifiers: null);

        Assert.NotNull(verified);
        Assert.True(verified!.GetParameters()[3].HasDefaultValue);
        Assert.Equal(false, verified.GetParameters()[3].DefaultValue);
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
        Assert.Contains("monitor.ModelRoutingEnabled", source, StringComparison.Ordinal);
        Assert.Contains("chrome.TrySelectModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("ChromeHidden", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForChatReadyAsync(chrome, recoveredTab", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReacquisitionNeverSendsFollowUpWhileRecoveredConversationIsGenerating()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");

        Assert.Contains("if (!recoveredState.IsGenerating)", source, StringComparison.Ordinal);
        Assert.Contains("&& !recoveredState.IsGenerating", source, StringComparison.Ordinal);
        Assert.Contains("MonitorTabReboundGenerating", source, StringComparison.Ordinal);
        Assert.Contains("without sending a follow-up", source, StringComparison.Ordinal);
        Assert.Contains("chrome.SendChatMessageVerifiedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", source, StringComparison.Ordinal);
        Assert.Contains("RestartFollowUpSent", source, StringComparison.Ordinal);
        Assert.Contains("RestartFollowUpFailed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageAsync(tab, followUp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRecoveryRestoresRunningMonitorBeforeEnteringWorkerLoop()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var recoveryIndex = source.IndexOf("MonitorTabRecoveryService.EnsureMonitorTabAsync(", StringComparison.Ordinal);
        var workerIndex = source.IndexOf("MonitorLoopAsync(recovery.Tab, monitor.Id", StringComparison.Ordinal);

        Assert.True(recoveryIndex >= 0, "Expected saved-monitor recovery during startup.");
        Assert.True(workerIndex > recoveryIndex, "Expected worker loop start only after saved conversation recovery.");
    }

    [Fact]
    public void StartupRecoveryDoesNotLaunchDuplicateMonitorWorker()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.Contains("if (_monitorTasks.ContainsKey(savedMonitor.Id))", source, StringComparison.Ordinal);
        Assert.Contains("TryBeginMonitorStartup(savedMonitor.Id)", source, StringComparison.Ordinal);
        Assert.Contains("_monitorTasks.TryAdd(savedMonitor.Id, task)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartRecoveryPreservesOnlyStableConversationIdentity()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");

        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url)", source, StringComparison.Ordinal);
        Assert.Contains("SavedMonitorTabResolver.Resolve(monitor, tabs)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault(t =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.FirstOrDefault()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorRecoveryNeverUsesAssistantTurnAsDeliveryReceipt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorTabRecoveryService.cs");

        Assert.Contains("SendChatMessageVerifiedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LastAssistantText", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GPTDeskTop.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
