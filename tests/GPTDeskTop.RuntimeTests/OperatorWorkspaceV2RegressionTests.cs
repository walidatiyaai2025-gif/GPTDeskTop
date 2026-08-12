namespace GPTDeskTop.RuntimeTests;

public sealed class OperatorWorkspaceV2RegressionTests
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
    public void MainWorkspacePrioritizesOpenTabsAndMonitorsWhileDiagnosticsAreOnDemand()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("root.RowStyles[2].Height = 100F", source, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[3].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("Live Monitor & History", source, StringComparison.Ordinal);
        Assert.Contains("development.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("Development controls and sent-message catalog are available from ☰ Commands", source, StringComparison.Ordinal);
        Assert.Contains("BuildDevelopmentFooterText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StoredHistoryIsRemovedFromMainAndRehostedAsOnDemandTab()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("OfType<HistoryWorkspaceControl>()", source, StringComparison.Ordinal);
        Assert.Contains("PrepareHistoryForOnDemand(history)", source, StringComparison.Ordinal);
        Assert.Contains("history.Parent?.Controls.Remove(history)", source, StringComparison.Ordinal);
        Assert.Contains("ExpandableWorkspaceLayout.UseHostManagedHeight(history)", source, StringComparison.Ordinal);
        Assert.Contains("new TabControl", source, StringComparison.Ordinal);
        Assert.Contains("new TabPage(\"Live Activity\")", source, StringComparison.Ordinal);
        Assert.Contains("new TabPage(\"Stored History\")", source, StringComparison.Ordinal);
        Assert.Contains("historyPage.Controls.Add(history)", source, StringComparison.Ordinal);
        Assert.Contains("history.Dock = DockStyle.Fill", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RehostingHistoryDoesNotMutatePersistedExpandedState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var prepareStart = source.IndexOf("private static void PrepareHistoryForOnDemand", StringComparison.Ordinal);
        var runtimePrepareStart = source.IndexOf("private static void PrepareRuntimeHealthForOnDemand", prepareStart, StringComparison.Ordinal);
        var prepare = source[prepareStart..runtimePrepareStart];

        Assert.Contains("historyBody.Visible = true", prepare, StringComparison.Ordinal);
        Assert.Contains("toggle.Visible = false", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("history.IsExpanded =", prepare, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthAndSupportDiagnosticsAreRemovedFromMainAndRehostedOnDemand()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("OfType<RuntimeHealthControl>()", source, StringComparison.Ordinal);
        Assert.Contains("OfType<SupportDiagnosticsControl>()", source, StringComparison.Ordinal);
        Assert.Contains("PrepareRuntimeHealthForOnDemand(runtimeHealth, supportDiagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("ExpandableWorkspaceLayout.UseHostManagedHeight(runtimeHealth)", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.Parent?.Controls.Remove(runtimeHealth)", source, StringComparison.Ordinal);
        Assert.Contains("supportDiagnostics.Parent?.Controls.Remove(supportDiagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("new TabPage(\"Runtime Health\")", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHost.Controls.Add(runtimeHealth)", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHost.Controls.Add(supportDiagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("tabs.TabPages.Add(runtimePage)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RehostingRuntimeHealthDoesNotMutatePersistedExpandedState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var prepareStart = source.IndexOf("private static void PrepareRuntimeHealthForOnDemand", StringComparison.Ordinal);
        var windowStart = source.IndexOf("private static Form BuildLiveMonitorWindow", prepareStart, StringComparison.Ordinal);
        var prepare = source[prepareStart..windowStart];

        Assert.Contains("runtimeHealth.Dock = DockStyle.Top", prepare, StringComparison.Ordinal);
        Assert.Contains("supportDiagnostics.Dock = DockStyle.Top", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeHealth.IsExpanded =", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("supportDiagnostics.Visible =", prepare, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandsMenuOpensLiveMonitorWindowAndKeepsMessagesOnDemand()
    {
        var menu = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("Live Monitor & History", menu, StringComparison.Ordinal);
        Assert.Contains("OperatorWorkspaceV2Experience.ShowLiveMonitor((MainForm)form)", menu, StringComparison.Ordinal);
        Assert.Contains("AddButtonCommand(developmentMenu, \"Messages\"", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentConnectionFailureEscalatesFromRefreshToSameConversationReopen()
    {
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var recoveryStart = chrome.IndexOf("private async Task<bool> RecoverMonitorTabAsync", StringComparison.Ordinal);
        var helperStart = chrome.IndexOf("private async Task<List<ChromeTab>?> TryGetLiveTabsAsync", recoveryStart, StringComparison.Ordinal);
        var recovery = chrome[recoveryStart..helperStart];

        Assert.Contains("RefreshConversationTabAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("ReopenConversationTabAsync", recovery, StringComparison.Ordinal);
        Assert.True(
            recovery.IndexOf("RefreshConversationTabAsync", StringComparison.Ordinal)
            < recovery.IndexOf("ReopenConversationTabAsync", StringComparison.Ordinal));
        Assert.Contains("CloseTabAsync(staleTab", chrome, StringComparison.Ordinal);
        Assert.Contains("WaitForReadableConversationStateAsync", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatGptRenderedErrorCreatesFreshChatWithConfigurableContinuationAndSameMonitorHandoff()
    {
        var monitor = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("ChatGptErrorContinuationMessage", monitor, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"ChatGptError\"", monitor, StringComparison.Ordinal);
        Assert.Contains("successStatus: \"RecoveredFromChatGptError\"", monitor, StringComparison.Ordinal);
        Assert.Contains("incrementRotationCount: false", monitor, StringComparison.Ordinal);
        Assert.Contains("ChatGptErrorContinuationMessage", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentityIsVersionTwo()
    {
        var app = ReadSource("src", "GPTDeskTop", "GPTDeskTop.csproj");
        var setup = ReadSource("src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj");
        var build = ReadSource("src", "GPTDeskTop.Build", "GPTDeskTop.Build.csproj");

        Assert.Contains("<Version>2.0.0</Version>", app, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.0.0.0</AssemblyVersion>", app, StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.0</Version>", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop Setup v2.0.0", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop v2.0.0", build, StringComparison.Ordinal);
    }
}
