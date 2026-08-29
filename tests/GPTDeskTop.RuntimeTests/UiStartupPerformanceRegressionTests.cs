namespace GPTDeskTop.RuntimeTests;

public sealed class UiStartupPerformanceRegressionTests
{
    [Fact]
    public void ColdStartupDoesNotConstructDuplicateHistoryWorkspaceOrSupportDiagnosticsByDefault()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.DoesNotContain("var historyWorkspace = new HistoryWorkspaceControl", program, StringComparison.Ordinal);
        Assert.Contains("SupportDiagnosticsControl? supportDiagnostics = null;", program, StringComparison.Ordinal);
        Assert.Contains("void EnsureSupportDiagnostics()", program, StringComparison.Ordinal);
        Assert.Contains("if (runtimeHealth.IsExpanded)", program, StringComparison.Ordinal);
        Assert.Contains("EnsureSupportDiagnostics();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsEntryUsesExplicitOneTimeInstallationWithoutIdleOrModuleInitializer()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var projects = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");

        Assert.Contains("ProjectMonitorUiBootstrap.Install(mainForm);", program, StringComparison.Ordinal);
        Assert.Contains("internal static void Install(MainForm main)", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("[ModuleInitializer]", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle +=", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle -=", projects, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsAndGitHubHeavyUiRemainLazyUntilOperatorInvocation()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var projects = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.DoesNotContain("new ProjectMonitorDashboardControl", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", program, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", projects, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, ProjectsDestination, () => ProjectMonitorUiBootstrap.CreateEmbeddedProjectsSurface(main))", shell, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, GitSettingsDestination, () => GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main))", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupBudgetIsRecordedAndBoundedWithoutBlockingLaunch()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("var startupTimer = Stopwatch.StartNew();", program, StringComparison.Ordinal);
        Assert.Contains("Runtime.LastUiStartupMs", program, StringComparison.Ordinal);
        Assert.Contains("Runtime.LastUiStartupBudget", program, StringComparison.Ordinal);
        Assert.Contains("startupTimer.ElapsedMilliseconds <= 3000 ? \"PASS\" : \"WARN\"", program, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(RepositoryPath(segments));

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
