namespace GPTDeskTop.RuntimeTests;

public sealed class LayoutPersistenceUiRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWindowPersistsBoundsStateAndSplitterRatiosWithoutBlockingShutdown()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("Ui.Main.WindowBounds", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Main.WindowState", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Main.WorkspaceSplitRatio", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Main.DiagnosticsSplitRatio", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource(TimeSpan.FromSeconds(2))", source, StringComparison.Ordinal);
        Assert.Contains("await PersistOperatorLayoutAsync(layoutTimeout.Token)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistOperatorLayoutAsync().GetAwaiter().GetResult()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedWindowBoundsAreRestoredOnlyWhenVisibleOnAConnectedScreen()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("IsBoundsVisible(savedBounds)", source, StringComparison.Ordinal);
        Assert.Contains("Screen.AllScreens.Any", source, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea.IntersectsWith(bounds)", source, StringComparison.Ordinal);
        Assert.Contains("ClampBoundsToWorkingArea", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSplitterStateFallsBackToResponsiveDefaults()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("ratio is < 0.15 or > 0.85", source, StringComparison.Ordinal);
        Assert.Contains("SetSplitRatio(_workspaceSplit, 0.42)", source, StringComparison.Ordinal);
        Assert.Contains("SetSplitRatio(_diagnosticsSplit, 0.48)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentDashboardExposesPersistableExpandedState()
    {
        var dashboard = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentTaskDashboardControl.cs");
        Assert.Contains("public bool IsExpanded", dashboard, StringComparison.Ordinal);
        Assert.Contains("public event EventHandler? ExpandedChanged", dashboard, StringComparison.Ordinal);
        Assert.Contains("private void ToggleExpanded() => IsExpanded = !IsExpanded;", dashboard, StringComparison.Ordinal);
    }
}