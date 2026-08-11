namespace GPTDeskTop.RuntimeTests;

public sealed class MainWindowLayoutVisibilityRegressionTests
{
    private static string ReadUiSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MainWindowDockOrderReservesSpaceForTopAndBottomSurfaces()
    {
        var source = ReadUiSource();
        Assert.Contains("if (form is MainForm)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureMainWindowDockOrder(form);", source, StringComparison.Ordinal);
        Assert.Contains("new Control?[] { mainContent, history, support, runtime, development }", source, StringComparison.Ordinal);
        Assert.Contains("form.Controls.SetChildIndex(desired[index], index);", source, StringComparison.Ordinal);
        Assert.Contains("WinForms docks direct children in reverse z-order", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentPlanActionsReceiveReadableMinimumWidths()
    {
        var source = ReadUiSource();
        Assert.Contains("EnhanceDevelopmentDashboard", source, StringComparison.Ordinal);
        Assert.Contains("\"Messages\" => 96", source, StringComparison.Ordinal);
        Assert.Contains("\"Schedule\" => 96", source, StringComparison.Ordinal);
        Assert.Contains("\"Collapse\" => 100", source, StringComparison.Ordinal);
        Assert.Contains("actions.AutoScroll = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthNeverShrinksActionsBackToTruncatingColumns()
    {
        var source = ReadUiSource();
        Assert.DoesNotContain("compact ? 72 : 86", source, StringComparison.Ordinal);
        Assert.Contains("SetAbsoluteColumn(header, 4, Scale(header, 102))", source, StringComparison.Ordinal);
        Assert.Contains("SetAbsoluteColumn(header, 5, Scale(header, 102))", source, StringComparison.Ordinal);
        Assert.Contains("SetAbsoluteColumn(header, 6, Scale(header, 92))", source, StringComparison.Ordinal);
        Assert.Contains("SetAbsoluteColumn(header, 7, Scale(header, 112))", source, StringComparison.Ordinal);
        Assert.Contains("\"Refresh\" => 90", source, StringComparison.Ordinal);
        Assert.Contains("\"Details\" => 94", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LongHistoryAndMainActionsAreProtectedFromEllipsis()
    {
        var source = ReadUiSource();
        Assert.Contains("\"Edit Selected Monitor\" => 164", source, StringComparison.Ordinal);
        Assert.Contains("\"Copy Selected\" => 118", source, StringComparison.Ordinal);
        Assert.Contains("\"Export Visible CSV\" => 142", source, StringComparison.Ordinal);
        Assert.Contains("button.AutoEllipsis = false", source, StringComparison.Ordinal);
    }
}
