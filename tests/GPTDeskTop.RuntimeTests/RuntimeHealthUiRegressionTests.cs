namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeHealthUiRegressionTests
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
    public void RuntimeHealthControlIsCompactDpiSafeAndAccessible()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("private const int CollapsedHeight = 62;", source, StringComparison.Ordinal);
        Assert.Contains("private const int ExpandedHeight = 188;", source, StringComparison.Ordinal);
        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Runtime health and connection center\"", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Refresh runtime health\"", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Repair recovery blocker\"", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Retry pending crash recovery\"", source, StringComparison.Ordinal);
        Assert.Contains("_tabsValue.AccessibleName = \"Open ChatGPT conversation count\"", source, StringComparison.Ordinal);
        Assert.Contains("Height = _expanded ? ExpandedHeight : CollapsedHeight", source, StringComparison.Ordinal);
        Assert.Contains("keyData == Keys.F5", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthRefreshIsBoundedReadOnlyAndDuplicateSafe()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("if (_loading || IsDisposed || Disposing) return;", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("ProbeChromeAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProbeDatabaseAsync", source, StringComparison.Ordinal);
        Assert.Contains("_chrome.GetTabsAsync", source, StringComparison.Ordinal);
        Assert.Contains("_database.GetSavedMonitorsAsync", source, StringComparison.Ordinal);
        Assert.Contains("_database.GetSettingAsync(\"CrashRecoveryPending\"", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveMonitorAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitorAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopMonitorAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthCountsOnlyStableConversationTabs()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("CreateMetricCard(\"Conversations\", _tabsValue)", source, StringComparison.Ordinal);
        Assert.Contains("var conversationTabs = tabs.Count(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url));", source, StringComparison.Ordinal);
        Assert.Contains("conversationTabs,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.Count(tab => RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("Open stable ChatGPT conversations visible through CDP.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthSurfacesRecoveryBlockersAndDelegatesMutationToRepairDialog()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("CreateMetricCard(\"Recovery\", _recoveryValue)", source, StringComparison.Ordinal);
        Assert.Contains("Blocked ({snapshot.InvalidMonitorIdentityCount})", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.CrashRecoveryPending ? \"Pending\" : \"Clear\"", source, StringComparison.Ordinal);
        Assert.Contains("new MonitorIdentityRepairForm(_chrome, _database)", source, StringComparison.Ordinal);
        Assert.Contains("if (form.ShowDialog(FindForm()) != DialogResult.OK) return;", source, StringComparison.Ordinal);
        Assert.Contains("await RefreshAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthAllowsOnlySafeExplicitPendingRecoveryRetry()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("_retryRecoveryButton.Enabled = !_recoveryRetrying", source, StringComparison.Ordinal);
        Assert.Contains("&& recoveryPending", source, StringComparison.Ordinal);
        Assert.Contains("&& invalidMonitorCount == 0", source, StringComparison.Ordinal);
        Assert.Contains("RetryPendingRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("monitors.Any(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url))", source, StringComparison.Ordinal);
        Assert.Contains("Retry Crash Recovery", source, StringComparison.Ordinal);
        Assert.Contains("CrashRecoveryMode.PendingRetry", source, StringComparison.Ordinal);
        Assert.Contains("monitors with persisted success receipts are not sent again", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashRecoveryMode.FreshCrashReset", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthTracksRunningMonitorChangesWithoutOwningWorkers()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("_monitor.RunningStateChanged += OnRunningStateChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.RunningStateChanged -= OnRunningStateChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.IsMonitorRunning", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramPersistsRuntimeHealthExpansionState()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("new RuntimeHealthControl(chrome, monitor, database)", source, StringComparison.Ordinal);
        Assert.Contains("Ui.RuntimeHealth.Expanded", source, StringComparison.Ordinal);
        Assert.Contains("Program.PersistRuntimeHealthState", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.ExpandedChanged", source, StringComparison.Ordinal);
    }
}