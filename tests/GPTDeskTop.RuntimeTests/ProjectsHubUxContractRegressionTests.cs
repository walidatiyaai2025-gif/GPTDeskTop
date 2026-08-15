namespace GPTDeskTop.RuntimeTests;

public sealed class ProjectsHubUxContractRegressionTests
{
    [Fact]
    public void OneProjectsHubReplacesUserFacingLegacyMonitorCrudNavigation()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var hub = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");
        var dashboard = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorDashboardControl.cs");
        var obsoleteConsolidation = RepositoryPath("src", "GPTDeskTop", "UI", "ProjectsHubNavigationConsolidation.cs");

        Assert.False(File.Exists(obsoleteConsolidation));
        Assert.Contains("ProjectMonitorUiBootstrap.Install(mainForm);", program, StringComparison.Ordinal);
        Assert.Contains("internal static void Install(MainForm main)", hub, StringComparison.Ordinal);
        Assert.Contains("Text = \"Projects\"", hub, StringComparison.Ordinal);
        Assert.DoesNotContain("[ModuleInitializer]", hub, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle +=", hub, StringComparison.Ordinal);
        Assert.Contains("private static ProjectMonitorDashboardForm? _dashboardForm", hub, StringComparison.Ordinal);
        Assert.Contains("Text = \"New Project Monitor\"", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsAndGitHubHeavyUiRemainLazyUntilOperatorOpensThem()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var hub = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");

        Assert.DoesNotContain("new ProjectMonitorDashboardForm", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", program, StringComparison.Ordinal);

        var showProjectsIndex = hub.IndexOf("private static void ShowProjectsHub", StringComparison.Ordinal);
        var dashboardCtorIndex = hub.IndexOf("new ProjectMonitorDashboardForm", StringComparison.Ordinal);
        Assert.True(showProjectsIndex >= 0 && dashboardCtorIndex > showProjectsIndex);

        var showGitIndex = hub.IndexOf("private static void ShowGitSettings", StringComparison.Ordinal);
        var githubCtorIndex = hub.IndexOf("new GitHubIntegrationControl", StringComparison.Ordinal);
        Assert.True(showGitIndex >= 0 && githubCtorIndex > showGitIndex);
    }

    [Fact]
    public void SilentGitHubPreflightUsesStoredRepositoryCredentialBeforeAnyChatIsCreated()
    {
        var wizard = ReadSource("src", "GPTDeskTop", "Services", "NewProjectMonitorWizardService.cs");
        var hub = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");

        Assert.Contains("settings.ResolveToken(draft.Repository)", wizard, StringComparison.Ordinal);
        Assert.Contains("ValidateAsync(NewProjectMonitorDraft draft", wizard, StringComparison.Ordinal);
        Assert.Contains("preflight = await wizardService.ValidateAsync(wizard.Draft)", hub, StringComparison.Ordinal);
        Assert.Contains("if (!preflight.Success)", hub, StringComparison.Ordinal);
        Assert.Contains("if (preflight.RequiresCredentialUi)", hub, StringComparison.Ordinal);

        var validationIndex = hub.IndexOf("preflight = await wizardService.ValidateAsync(wizard.Draft)", StringComparison.Ordinal);
        var creatorIndex = hub.IndexOf("new NewProjectMonitorCreationService", StringComparison.Ordinal);
        Assert.True(validationIndex >= 0 && creatorIndex > validationIndex);
    }

    [Fact]
    public void FreshChatCreationPersistsStableConversationThenStartsProjectMonitor()
    {
        var creation = ReadSource("src", "GPTDeskTop", "Services", "NewProjectMonitorCreationService.cs");
        var workflow = ReadSource("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs");

        Assert.Contains("NewChatMonitorWorkflowService", creation, StringComparison.Ordinal);
        Assert.Contains("ProjectBootstrapPromptBuilder", creation, StringComparison.Ordinal);
        Assert.Contains("ConversationUrl", creation, StringComparison.Ordinal);
        Assert.Contains("ProjectState", creation, StringComparison.Ordinal);

        var freshChat = workflow.IndexOf("CreateFreshChatTabAsync", StringComparison.Ordinal);
        var verifiedSend = workflow.IndexOf("SendInitialMessageVerifiedAsync(openedTab", StringComparison.Ordinal);
        var stableUrl = workflow.IndexOf("ResolveStableConversationAsync(", verifiedSend, StringComparison.Ordinal);
        var register = workflow.IndexOf("RegisterMonitorIfConversationAvailableAsync", StringComparison.Ordinal);
        var start = workflow.IndexOf("StartMonitorAsync(savedMonitor, stableTab)", StringComparison.Ordinal);

        Assert.True(freshChat >= 0);
        Assert.True(verifiedSend > freshChat);
        Assert.True(stableUrl > verifiedSend);
        Assert.True(register > stableUrl);
        Assert.True(start > register);
    }

    [Fact]
    public void SettingsHasNoDeferredRenderMutationBootstrap()
    {
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var obsolete = RepositoryPath("src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs");

        Assert.False(File.Exists(obsolete));
        Assert.Contains("_tabs.Enabled = true;", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("_tabs.Enabled = !busy;", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle +=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle -=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("EnabledChanged +=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibleChanged +=", settings, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(RepositoryPath(segments));

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
