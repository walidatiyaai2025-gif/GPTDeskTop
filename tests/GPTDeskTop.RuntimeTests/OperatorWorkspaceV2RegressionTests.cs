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
    public void MainWorkspacePrioritizesOpenTabsAndKeepsDiagnosticsInsideMainFormOnDemand()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("root.RowStyles[2].Height = 100F", source, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[3].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("diagnostics.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("_diagnostics.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("development.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("Development controls and sent-message catalog are available from ☰ Commands", source, StringComparison.Ordinal);
        Assert.Contains("BuildDevelopmentFooterText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceActivationUsesCanonicalMainFormHistoryWithoutCreatingTabOrWindowCopies()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.DoesNotContain("OfType<HistoryWorkspaceControl>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareHistoryForOnDemand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TabPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.Contains("root.GetRow(control) == 3", source, StringComparison.Ordinal);
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

        Assert.Contains("DevelopmentTaskDashboardControl", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportDiagnosticsControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryWorkspaceControl", source, StringComparison.Ordinal);
        Assert.Contains("SupportDiagnosticsControl? supportDiagnostics = null;", program, StringComparison.Ordinal);
        Assert.Contains("void EnsureSupportDiagnostics()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthRemainsOwnedByMainFormAndIsNeverReparentedIntoAnotherForm()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("var runtimeHealth = Descendants(form).OfType<RuntimeHealthControl>().FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("Runtime Health and the lazily-created Support Diagnostics remain direct children", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeHealth.Parent?.Controls.Remove(runtimeHealth)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareRuntimeHealthForOnDemand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildLiveMonitorWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeHealth.IsExpanded =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportDiagnosticsRemainsOwnedByProgramsCanonicalLazyFactory()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.DoesNotContain("BuildSupportPlaceholder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SupportDiagnosticsControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_owner.ControlAdded", source, StringComparison.Ordinal);
        Assert.Contains("SupportDiagnosticsControl? supportDiagnostics = null;", program, StringComparison.Ordinal);
        Assert.Contains("void EnsureSupportDiagnostics()", program, StringComparison.Ordinal);
        Assert.Contains("supportDiagnostics = new SupportDiagnosticsControl", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandsMenuOpensLiveMonitorSurfaceInsideMainFormAndKeepsMessagesOnDemand()
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
    public void ReleaseIdentityIsVersionTwoPointZeroPointOne()
    {
        var app = ReadSource("src", "GPTDeskTop", "GPTDeskTop.csproj");
        var setup = ReadSource("src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj");
        var build = ReadSource("src", "GPTDeskTop.Build", "GPTDeskTop.Build.csproj");

        Assert.Contains("<Version>2.0.7</Version>", app, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.0.7.0</AssemblyVersion>", app, StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.7</Version>", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop Setup v2.0.7", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop v2.0.7", build, StringComparison.Ordinal);
    }
}
