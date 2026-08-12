namespace GPTDeskTop.RuntimeTests;

public sealed class CompactTopCommandMenuUiRegressionTests
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
    public void AllTopLevelOperatorActionsAreConsolidatedIntoOneCommandMenu()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("new ToolStripMenuItem(\"☰ Commands\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Development Plan\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Runtime Health\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Browser\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Monitors\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Runtime\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Application\")", source, StringComparison.Ordinal);
        Assert.Contains("Focus Live Activity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyToolbarAndEmbeddedActionRowsAreRemovedFromVisibleLayout()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("CollapseLegacyMainToolbar(form)", source, StringComparison.Ordinal);
        Assert.Contains("toolbar.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("CollapseTableRow(layout, toolbar)", source, StringComparison.Ordinal);
        Assert.Contains("actionRow.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("HideHeaderButtonAndColumn(button)", source, StringComparison.Ordinal);
        Assert.Contains("development.IsExpanded = false", source, StringComparison.Ordinal);
        Assert.Contains("health.IsExpanded = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenMonitorToolbarActionsRemainReachableFromCommandsMenu()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("AddButtonCommand(monitorMenu, \"New Chat + Monitor\", sources.NewChatMonitor);", source, StringComparison.Ordinal);
        Assert.Contains("RequiredButton(mainButtons, \"New Chat + Monitor\")", source, StringComparison.Ordinal);
        Assert.Contains("Button NewChatMonitor,", source, StringComparison.Ordinal);
        Assert.Contains("NewChatMonitor, AddMonitor, EditMonitor, DeleteMonitor", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuCommandsKeepExistingButtonsAsSingleBehaviorOwner()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("Tag = source", source, StringComparison.Ordinal);
        Assert.Contains("InvokeButtonCommand(source)", source, StringComparison.Ordinal);
        Assert.Contains("source.GetType().GetMethod(", source, StringComparison.Ordinal);
        Assert.Contains("\"OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("BindingFlags.Instance | BindingFlags.NonPublic", source, StringComparison.Ordinal);
        Assert.Contains("menuItem.Enabled = source.Enabled", source, StringComparison.Ordinal);
        Assert.Contains("The existing Buttons remain the single command/event source", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactStatusBarsPreserveDetailsOnDemandWithOnePhysicalHeightOwner()
    {
        var compact = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");
        var layout = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("development.IsExpanded ? \"Hide Details\" : \"Show Details\"", compact, StringComparison.Ordinal);
        Assert.Contains("health.IsExpanded ? \"Hide Details\" : \"Show Details\"", compact, StringComparison.Ordinal);
        Assert.Contains("ExpandableWorkspaceLayout.EnableCompactOperatorLayout(development);", compact, StringComparison.Ordinal);
        Assert.Contains("ExpandableWorkspaceLayout.EnableCompactOperatorLayout(health);", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactDevelopmentHeight", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDevelopmentHeight", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyHealthHeight", compact, StringComparison.Ordinal);

        Assert.Contains("private const int CompactDevelopmentCollapsedHeight = 58;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int CompactDevelopmentExpandedHeight = 118;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int CompactRuntimeHealthCollapsedHeight = 58;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int CompactRuntimeHealthExpandedHeight = 140;", layout, StringComparison.Ordinal);
        Assert.Contains("CompactOperatorControls.TryGetValue(control, out _)", layout, StringComparison.Ordinal);
        Assert.Contains("control.SizeChanged += (_, _) => ApplyCurrentHeight();", layout, StringComparison.Ordinal);
        Assert.Contains("control.DpiChangedAfterParent += (_, _) => ApplyCurrentHeight();", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void NonCompactControlsRetainLegacyExpandableHeights()
    {
        var layout = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("private const int DevelopmentCollapsedHeight = 72;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int DevelopmentExpandedHeight = 178;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int RuntimeHealthCollapsedHeight = 62;", layout, StringComparison.Ordinal);
        Assert.Contains("private const int RuntimeHealthExpandedHeight = 188;", layout, StringComparison.Ordinal);
        Assert.Contains("compact ? CompactDevelopmentCollapsedHeight : DevelopmentCollapsedHeight", layout, StringComparison.Ordinal);
        Assert.Contains("compact ? CompactRuntimeHealthCollapsedHeight : RuntimeHealthCollapsedHeight", layout, StringComparison.Ordinal);
    }
}
