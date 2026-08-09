namespace GPTDeskTop.RuntimeTests;

public sealed class ResponsiveUiRegressionTests
{
    private static string ReadMainForm()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainForm.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWindowUsesDpiAndWorkingAreaAwareSizing()
    {
        var source = ReadMainForm();
        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(980, 680)", source, StringComparison.Ordinal);
        Assert.Contains("Screen.FromPoint(Cursor.Position).WorkingArea", source, StringComparison.Ordinal);
        Assert.Contains("ApplyInitialWindowLayout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumSize = new Size(1260, 820)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionBarCanGrowWhenGroupsWrap()
    {
        var source = ReadMainForm();
        Assert.Contains("root.RowStyles.Add(new RowStyle(SizeType.AutoSize))", source, StringComparison.Ordinal);
        Assert.Contains("AutoSizeMode = AutoSizeMode.GrowAndShrink", source, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(0, 66)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceSplittersAreRatioBasedAndSafelyClamped()
    {
        var source = ReadMainForm();
        Assert.Contains("private readonly SplitContainer _workspaceSplit", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SplitContainer _diagnosticsSplit", source, StringComparison.Ordinal);
        Assert.Contains("SetSplitRatio(_workspaceSplit, 0.42)", source, StringComparison.Ordinal);
        Assert.Contains("ClampResponsiveSplitters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SplitterDistance = 620", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SplitterDistance = 650", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyListsShowOperatorGuidanceInsteadOfBlankPanels()
    {
        var source = ReadMainForm();
        Assert.Contains("No ChatGPT tabs are open", source, StringComparison.Ordinal);
        Assert.Contains("No saved monitors yet", source, StringComparison.Ordinal);
        Assert.Contains("No stored history yet", source, StringComparison.Ordinal);
        Assert.Contains("CreateGridHost(_tabsGrid, _tabsEmptyState)", source, StringComparison.Ordinal);
        Assert.Contains("CreateGridHost(_monitorsGrid, _monitorsEmptyState)", source, StringComparison.Ordinal);
        Assert.Contains("CreateGridHost(_historyGrid, _historyEmptyState)", source, StringComparison.Ordinal);
        Assert.Contains("UpdateEmptyStates", source, StringComparison.Ordinal);
    }
}