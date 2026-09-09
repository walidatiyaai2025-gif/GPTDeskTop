namespace GPTDeskTop.RuntimeTests;

public sealed class UiStartupPerformanceRegressionTests
{
    [Fact]
    public void ColdStartupConstructsOnlyMonitorOnlySurface()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var gate = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");

        Assert.Contains("MonitorOnlyStartupGate.Run(database);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new HistoryWorkspaceControl", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportDiagnosticsControl", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainForm(", program, StringComparison.Ordinal);
        Assert.Contains("using var form = new SimpleMonitorForm(database);", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsBootstrapRemainsExplicitButIsNotInstalledByMonitorOnlyStartup()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var projects = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");

        Assert.DoesNotContain("ProjectMonitorUiBootstrap.Install(", program, StringComparison.Ordinal);
        Assert.Contains("internal static void Install(MainForm main)", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("[ModuleInitializer]", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle +=", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle -=", projects, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsAndGitHubHeavyUiRemainLazyUntilOperatorInvocation()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.DoesNotContain("new ProjectMonitorDashboardControl", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", program, StringComparison.Ordinal);

        var projectsCaseIndex = shell.IndexOf("case ProjectsDestination:", StringComparison.Ordinal);
        var projectsFactoryIndex = shell.IndexOf("ProjectMonitorUiBootstrap.CreateEmbeddedProjectsSurface(main)", projectsCaseIndex, StringComparison.Ordinal);
        Assert.True(projectsCaseIndex >= 0 && projectsFactoryIndex > projectsCaseIndex);

        var gitCaseIndex = shell.IndexOf("case GitSettingsDestination:", StringComparison.Ordinal);
        var gitFactoryIndex = shell.IndexOf("GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main)", gitCaseIndex, StringComparison.Ordinal);
        Assert.True(gitCaseIndex >= 0 && gitFactoryIndex > gitCaseIndex);

        Assert.Contains("GetOrCreate(registration, ProjectsDestination", shell, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, GitSettingsDestination", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyStartupAvoidsLegacyStartupInstrumentationAndBlockingResumeWork()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("MonitorOnlyStartupGate.Run(database);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("startupTimer", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.LastUiStartupMs", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime.LastUiStartupBudget", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeIfActiveAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitorAsync", program, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(RepositoryPath(segments));

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
