namespace GPTDeskTop.RuntimeTests;

public sealed class PremiumProductClosureUiTests
{
    private static readonly string UiDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "UI"));

    [Fact]
    public void OwnedPremiumDestinationsRouteThroughExactlyOneContentHost()
    {
        var shell = Read("PremiumRuntimeShellExperience.cs");
        Assert.Contains("host.Controls.Clear();", shell, StringComparison.Ordinal);
        Assert.Contains("host.Controls.Add(surface);", shell, StringComparison.Ordinal);
        Assert.Contains("return host.Controls.Count == 1;", shell, StringComparison.Ordinal);
        Assert.Contains("ProjectMonitorUiBootstrap.CreateEmbeddedProjectsSurface(main)", shell, StringComparison.Ordinal);
        Assert.Contains("new DevelopmentMessagesWorkspaceControl(registration.DevelopmentDashboard.RuntimeBinding)", shell, StringComparison.Ordinal);
        Assert.Contains("GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main)", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsRetiresItsSecondaryPersistentWorkspaceForm()
    {
        var bootstrap = Read("ProjectMonitorUiBootstrap.cs");
        Assert.DoesNotContain("ProjectMonitorDashboardForm", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("_dashboardForm", bootstrap, StringComparison.Ordinal);
        Assert.Contains("PremiumRuntimeShellExperience.NavigateTo(main, \"Projects\")", bootstrap, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(UiDirectory, "ProjectMonitorDashboardForm.cs")));

        var projects = Read("ProjectMonitorDashboardControl.cs");
        foreach (var header in new[] { "Project", "Status", "Progress", "Active tasks", "Health", "Branch", "Repository", "Updated", "Latest result" })
            Assert.Contains($"\"{header}\"", projects, StringComparison.Ordinal);
        Assert.Contains("Bound monitor:", projects, StringComparison.Ordinal);
        Assert.Contains("LATEST VERIFICATION EVIDENCE", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.Firebrick", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.DarkOrange", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.SeaGreen", projects, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentMessagesUsesEmbeddedRuntimeAndTrueMultilineEditing()
    {
        var dashboard = Read("DevelopmentTaskDashboardControl.cs");
        Assert.Contains("internal DevelopmentTaskRuntimeBinding RuntimeBinding", dashboard, StringComparison.Ordinal);
        Assert.Contains("NavigateTo(main, \"Development Messages\")", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", dashboard, StringComparison.Ordinal);

        var workspace = Read("DevelopmentMessagesWorkspaceControl.cs");
        Assert.Contains("DevelopmentTaskRuntimeBinding _binding", workspace, StringComparison.Ordinal);
        Assert.Contains("state.DeliveryReceipts.Values", workspace, StringComparison.Ordinal);
        Assert.Contains("DevelopmentTaskEngineStatus.Faulted", workspace, StringComparison.Ordinal);
        Assert.Contains("DevelopmentTaskScheduleSettingsControl", workspace, StringComparison.Ordinal);

        var catalog = Read("DevelopmentMessageCatalogControl.cs");
        Assert.Contains("Multiline = true", catalog, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", catalog, StringComparison.Ordinal);
        Assert.Contains("WordWrap = true", catalog, StringComparison.Ordinal);
        Assert.Contains("AutoSize = false", catalog, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(0, 120)", catalog, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Development message editor\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubSettingsRemainProtectedAndEmbedded()
    {
        var bootstrap = Read("GitHubIntegrationUiBootstrap.cs");
        Assert.Contains("PremiumRuntimeShellExperience.NavigateTo(main, \"GitHub / Git Settings\")", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CreateEmbeddedGitSettingsSurface", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", bootstrap, StringComparison.Ordinal);

        var integration = Read("GitHubIntegrationControl.cs");
        Assert.True(Count(integration, "UseSystemPasswordChar = true") >= 2);
        Assert.Contains("owner/repository", integration, StringComparison.Ordinal);
        Assert.Contains("Preferred branch", integration, StringComparison.Ordinal);
        Assert.Contains("Per-repository credentials", integration, StringComparison.Ordinal);
        Assert.Contains("Live validation", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void LockedPremiumPaletteAndTargetViewportMatrixStayCanonical()
    {
        var theme = Read("FluentTheme.cs");
        foreach (var rgb in new[]
        {
            "5, 14, 24", "9, 23, 38", "12, 29, 47", "7, 20, 34",
            "16, 40, 65", "10, 113, 255", "39, 130, 255", "11, 42, 74",
            "235, 243, 255", "135, 153, 179", "28, 48, 70",
            "52, 211, 153", "245, 158, 11", "248, 81, 96", "56, 189, 248"
        }) Assert.Contains(rgb, theme, StringComparison.Ordinal);

        foreach (var target in new[]
        {
            (1366, 768, 96),
            (1600, 900, 96),
            (1920, 1080, 96),
            (1920, 1080, 120)
        })
        {
            var physical = new System.Drawing.Size(target.Item1, target.Item2);
            Assert.True(GPTDeskTop.UI.PremiumRuntimeShellExperience.SupportsViewport(physical, target.Item3));
        }
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(UiDirectory, fileName));
    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
}
