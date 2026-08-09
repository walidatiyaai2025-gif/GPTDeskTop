namespace GPTDeskTop.RuntimeTests;

public sealed class HistoryWorkspaceUiRegressionTests
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
    public void HistoryWorkspaceIsCollapsibleDpiAwareAndAccessible()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "HistoryWorkspaceControl.cs");

        Assert.Contains("private const int CollapsedHeight = 56;", source, StringComparison.Ordinal);
        Assert.Contains("private const int ExpandedHeight = 330;", source, StringComparison.Ordinal);
        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Stored history explorer\"", source, StringComparison.Ordinal);
        Assert.Contains("_body.Visible = _expanded", source, StringComparison.Ordinal);
        Assert.Contains("Height = _expanded ? ExpandedHeight : CollapsedHeight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryWorkspaceExposesSearchFiltersCopyAndVisibleCsvExport()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "HistoryWorkspaceControl.cs");

        Assert.Contains("PlaceholderText = \"Search history…\"", source, StringComparison.Ordinal);
        Assert.Contains("HistoryWorkspaceLogic.Filter", source, StringComparison.Ordinal);
        Assert.Contains("Clear Filters", source, StringComparison.Ordinal);
        Assert.Contains("Copy Selected", source, StringComparison.Ordinal);
        Assert.Contains("Export Visible CSV", source, StringComparison.Ordinal);
        Assert.Contains("HistoryWorkspaceLogic.ToCsv(_visibleLogs)", source, StringComparison.Ordinal);
        Assert.Contains("new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryWorkspaceHasContextSafeKeyboardShortcuts()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "HistoryWorkspaceControl.cs");

        Assert.Contains("Keys.Control | Keys.F", source, StringComparison.Ordinal);
        Assert.Contains("keyData == Keys.F5", source, StringComparison.Ordinal);
        Assert.Contains("Keys.Control | Keys.C", source, StringComparison.Ordinal);
        Assert.Contains("_grid.ContainsFocus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramPersistsHistoryWorkspaceExpansionWithoutChangingRuntimeShutdownContract()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("new HistoryWorkspaceControl(database)", source, StringComparison.Ordinal);
        Assert.Contains("Ui.HistoryWorkspace.Expanded", source, StringComparison.Ordinal);
        Assert.Contains("Program.PersistHistoryWorkspaceState", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => FinalizeGracefulShutdownAsync(database, developmentRuntime))", source, StringComparison.Ordinal);
    }
}
