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
    public void CompactStatusBarsPreserveDetailsOnDemand()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");

        Assert.Contains("private const int CompactDevelopmentHeight = 58;", source, StringComparison.Ordinal);
        Assert.Contains("private const int CompactHealthHeight = 58;", source, StringComparison.Ordinal);
        Assert.Contains("development.IsExpanded ? \"Hide Details\" : \"Show Details\"", source, StringComparison.Ordinal);
        Assert.Contains("health.IsExpanded ? \"Hide Details\" : \"Show Details\"", source, StringComparison.Ordinal);
        Assert.Contains("development.Height = development.IsExpanded", source, StringComparison.Ordinal);
        Assert.Contains("health.Height = health.IsExpanded", source, StringComparison.Ordinal);
    }
}
