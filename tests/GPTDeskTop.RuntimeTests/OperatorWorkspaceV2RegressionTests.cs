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
    public void WorkspaceActivationUsesCanonicalMainFormHistoryAndHasNoEagerHistoryPrerequisite()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.DoesNotContain("OfType<HistoryWorkspaceControl>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareHistoryForOnDemand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TabPage(\"Stored History\")", source, StringComparison.Ordinal);
        Assert.Contains("new TabPage(\"Live Monitor & History\")", source, StringComparison.Ordinal);
        Assert.Contains("livePage.Controls.Add(diagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("BuildDiagnostics()", main, StringComparison.Ordinal);
        Assert.Contains("CreateSection(\"Live Activity\"", main, StringComparison.Ordinal);
        Assert.Contains("CreateSection(\"Stored History\"", main, StringComparison.Ordinal);
        Assert.Contains("HistoryWorkspaceControl duplicated a second grid", program, StringComparison.Ordinal);
        Assert.Contains("intentionally no longer constructed here", program, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceCanInstallBeforeLazySupportDiagnosticsExists()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        var prerequisiteStart = source.IndexOf("var development = Descendants(form)", StringComparison.Ordinal);
        var diagnosticsStart = source.IndexOf("// MainForm already owns the canonical diagnostics split", prerequisiteStart, StringComparison.Ordinal);
        Assert.True(prerequisiteStart >= 0 && diagnosticsStart > prerequisiteStart);
        var prerequisites = source[prerequisiteStart..diagnosticsStart];

        Assert.Contains("DevelopmentTaskDashboardControl", prerequisites, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthControl", prerequisites, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportDiagnosticsControl", prerequisites, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryWorkspaceControl", prerequisites, StringComparison.Ordinal);
        Assert.Contains("SupportDiagnosticsControl? supportDiagnostics = null;", program, StringComparison.Ordinal);
        Assert.Contains("void EnsureSupportDiagnostics()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthRemainsOnDemandWithoutMutatingPersistedExpandedState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var prepareStart = source.IndexOf("private static void PrepareRuntimeHealthForOnDemand", StringComparison.Ordinal);
        var windowStart = source.IndexOf("private static LiveWindowParts BuildLiveMonitorWindow", prepareStart, StringComparison.Ordinal);
        Assert.True(prepareStart >= 0 && windowStart > prepareStart);
        var prepare = source[prepareStart..windowStart];

        Assert.Contains("ExpandableWorkspaceLayout.UseHostManagedHeight(runtimeHealth)", prepare, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.Parent?.Controls.Remove(runtimeHealth)", prepare, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.Dock = DockStyle.Top", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeHealth.IsExpanded =", prepare, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportDiagnosticsAttachesOnlyAfterProgramsCanonicalLazyFactoryCreatesIt()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("BuildSupportPlaceholder(runtimeHealth)", source, StringComparison.Ordinal);
        Assert.Contains("Load Support Diagnostics", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.IsExpanded = true", source, StringComparison.Ordinal);
        Assert.Contains("_owner.ControlAdded += OnOwnerControlAdded", source, StringComparison.Ordinal);
        Assert.Contains("if (e.Control is SupportDiagnosticsControl)", source, StringComparison.Ordinal);
        Assert.Contains("_owner.Controls.OfType<SupportDiagnosticsControl>().FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("Never create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SupportDiagnosticsControl", source, StringComparison.Ordinal);
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
