namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentDashboardUiRegressionTests
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
    public void DashboardCanCollapseWithoutHidingLiveState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentTaskDashboardControl.cs");

        Assert.Contains("private const int CollapsedHeight = 72;", source, StringComparison.Ordinal);
        Assert.Contains("private const int ExpandedHeight = 178;", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Button _toggle", source, StringComparison.Ordinal);
        Assert.Contains("ToggleExpanded", source, StringComparison.Ordinal);
        Assert.Contains("_body.Visible = _expanded", source, StringComparison.Ordinal);
        Assert.Contains("Height = _expanded ? ExpandedHeight : CollapsedHeight", source, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(_status", source, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(_phase", source, StringComparison.Ordinal);
        Assert.Contains("header.Controls.Add(_countdown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardUsesSemanticLiveStatusStyling()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentTaskDashboardControl.cs");

        Assert.Contains("ApplyStatusStyle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.WarningSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.AccentSubtle", source, StringComparison.Ordinal);
        Assert.Contains("● {state.Status}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingLifecycleBindingsRemainPresent()
    {
        var dashboard = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentTaskDashboardControl.cs");
        var workspace = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentMessagesWorkspaceControl.cs");

        Assert.Contains("_binding.StartAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("_binding.PauseAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("_binding.ResumeAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("_binding.StopAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("NavigateTo(main, \"Development Messages\")", dashboard, StringComparison.Ordinal);
        Assert.Contains("DevelopmentMessageCatalogControl", workspace, StringComparison.Ordinal);
        Assert.Contains("DevelopmentTaskScheduleSettingsControl", workspace, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramDoesNotForceDashboardToConsume190Pixels()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("new DevelopmentTaskDashboardControl(developmentRuntime)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 190", source, StringComparison.Ordinal);
    }
}
