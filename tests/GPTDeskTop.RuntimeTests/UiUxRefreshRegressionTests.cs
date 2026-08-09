namespace GPTDeskTop.RuntimeTests;

public sealed class UiUxRefreshRegressionTests
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
    public void MainWorkspaceHasOperationalHierarchyAndDistinctQuickEditButton()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("ChatGPT monitoring, recovery and conversation automation", source, StringComparison.Ordinal);
        Assert.Contains("CreateMetricChip(\"Running\"", source, StringComparison.Ordinal);
        Assert.Contains("CreateActionGroup(\"BROWSER\"", source, StringComparison.Ordinal);
        Assert.Contains("CreateActionGroup(\"MONITOR\"", source, StringComparison.Ordinal);
        Assert.Contains("CreateActionGroup(\"RUNTIME\"", source, StringComparison.Ordinal);
        Assert.Contains("_quickMonitorSettingsButton", source, StringComparison.Ordinal);
        Assert.Contains("ReadOnly = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.Controls.Add(_monitorSettingsButton", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWorkspaceExposesSelectionAwareActionsAndRuntimeFormatting()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("UpdateActionStates", source, StringComparison.Ordinal);
        Assert.Contains("FormatMonitorCell", source, StringComparison.Ordinal);
        Assert.Contains("FormatHistoryCell", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.IsMonitorRunning", source, StringComparison.Ordinal);
        Assert.Contains("Use Edit Selected Monitor to change this value", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalSettingsAreGroupedByOperatorTask()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("new TabControl", source, StringComparison.Ordinal);
        Assert.Contains("CreateTab(\"Monitoring\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateTab(\"Rotation & Recovery\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateTab(\"Notifications\")", source, StringComparison.Ordinal);
        Assert.Contains("RotateAfterAssistantMessages", source, StringComparison.Ordinal);
        Assert.Contains("MessageCountRotationStartMessage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorSettingsAreGroupedWithoutDroppingExistingSemantics()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorSettingsForm.cs");

        Assert.Contains("CreateTab(\"General\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateTab(\"Rotation\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateTab(\"Model Routing\")", source, StringComparison.Ordinal);
        Assert.Contains("monitor.ConversationRotationEnabled = dialog.ConversationRotationEnabled", source, StringComparison.Ordinal);
        Assert.Contains("monitor.MaxConversationRotations = dialog.MaxConversationRotations", source, StringComparison.Ordinal);
        Assert.Contains("monitor.PreferredModel = dialog.PreferredModel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeProvidesReadableStatusAndGridPrimitives()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "FluentTheme.cs");

        Assert.Contains("SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("WarningSubtle", source, StringComparison.Ordinal);
        Assert.Contains("DataGridViewCellBorderStyle.SingleHorizontal", source, StringComparison.Ordinal);
        Assert.Contains("CreateSectionTitle", source, StringComparison.Ordinal);
        Assert.Contains("CreateMutedLabel", source, StringComparison.Ordinal);
    }
}
