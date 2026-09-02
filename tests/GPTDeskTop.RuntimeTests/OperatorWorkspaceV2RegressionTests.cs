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
    public void MainWorkspaceKeepsDiagnosticsInsideSingleMainWindowUntilRequested()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("diagnostics.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[2].Height = 100F", source, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[3].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("development.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("runtimeHealth.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("BuildDevelopmentFooterText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildLiveMonitorWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveMonitorCommandRevealsCanonicalDiagnosticsInsideMainForm()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");

        Assert.Contains("_diagnostics.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("_root.RowStyles[2].Height = 58F", source, StringComparison.Ordinal);
        Assert.Contains("_root.RowStyles[3].Height = 42F", source, StringComparison.Ordinal);
        Assert.Contains("_diagnostics.BringToFront()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceActivationKeepsCanonicalMainFormHistoryAndNoSecondHistoryPipeline()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.DoesNotContain("OfType<HistoryWorkspaceControl>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TabPage(\"Stored History\")", source, StringComparison.Ordinal);
        Assert.Contains("BuildDiagnostics()", main, StringComparison.Ordinal);
        Assert.Contains("CreateSection(\"Live Activity\"", main, StringComparison.Ordinal);
        Assert.Contains("CreateSection(\"Stored History\"", main, StringComparison.Ordinal);
        Assert.Contains("HistoryWorkspaceControl duplicated a second grid", program, StringComparison.Ordinal);
        Assert.Contains("intentionally no longer constructed here", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandsMenuUsesSingleSurfaceLiveMonitorAndKeepsMessagesOnDemand()
    {
        var menu = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

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
    public void ReleaseIdentityUsesCanonicalVersionTwoPointZeroPointFifteen()
    {
        var props = ReadSource("Directory.Build.props");
        var app = ReadSource("src", "GPTDeskTop", "GPTDeskTop.csproj");
        var setup = ReadSource("src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj");
        var build = ReadSource("src", "GPTDeskTop.Build", "GPTDeskTop.Build.csproj");
        var releaseWorkflow = ReadSource(".github", "workflows", "release-artifact.yml");

        Assert.Contains("<GPTDeskTopVersion>2.0.15</GPTDeskTopVersion>", props, StringComparison.Ordinal);
        Assert.Contains("<Version>$(GPTDeskTopVersion)</Version>", props, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>$(GPTDeskTopVersion).0</AssemblyVersion>", props, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>$(GPTDeskTopVersion).0</FileVersion>", props, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>2.0.0</Version>", app, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>2.0.0</Version>", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop Setup v$(GPTDeskTopVersion)", setup, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop v$(GPTDeskTopVersion)", build, StringComparison.Ordinal);
        Assert.Contains("-getProperty:Version", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("v2.0.0-build", releaseWorkflow, StringComparison.Ordinal);
    }
}
