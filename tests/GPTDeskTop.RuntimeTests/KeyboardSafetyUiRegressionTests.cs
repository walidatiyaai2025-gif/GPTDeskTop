namespace GPTDeskTop.RuntimeTests;

public sealed class KeyboardSafetyUiRegressionTests
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
    public void OperationalShortcutsAreExplicitAndContextSensitive()
    {
        var source = ReadMainForm();
        Assert.Contains("KeyPreview = true", source, StringComparison.Ordinal);
        Assert.Contains("KeyDown += MainForm_KeyDown", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.F5", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.N", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.E", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.Oemcomma", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.Delete && _monitorsGrid.ContainsFocus", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.Delete && _historyGrid.ContainsFocus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.KeyCode == Keys.Delete && _tabsGrid.ContainsFocus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DestructiveActionsDefaultToNoAndIdentifyTheirTargets()
    {
        var source = ReadMainForm();
        Assert.Contains("MessageBoxDefaultButton.Button2", source, StringComparison.Ordinal);
        Assert.Contains("The monitor will be stopped if necessary", source, StringComparison.Ordinal);
        Assert.Contains("Any unsent text in this tab will be lost", source, StringComparison.Ordinal);
        Assert.Contains("Delete this stored history entry?", source, StringComparison.Ordinal);
        Assert.Contains("Delete all stored history?", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardFocusStartsInTheOperationalWorkspace()
    {
        var source = ReadMainForm();
        Assert.Contains("FocusOperationalWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("_monitorsGrid.Focus()", source, StringComparison.Ordinal);
        Assert.Contains("_tabsGrid.Focus()", source, StringComparison.Ordinal);
        Assert.Contains("_launchChromeButton.Focus()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FormattingUsesNullableSafeCellStyleGuards()
    {
        var source = ReadMainForm();
        Assert.Contains("e.CellStyle is not { } style", source, StringComparison.Ordinal);
        Assert.Contains("e.ColumnIndex < 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.CellStyle.ForeColor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.CellStyle.Font", source, StringComparison.Ordinal);
    }
}