namespace GPTDeskTop.RuntimeTests;

public sealed class CompactSelectedMonitorUiRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }

    [Fact]
    public void SavedMonitorsOwnsAllFlexibleHeightAndSelectionUsesFixedStrip()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactSelectedMonitorExperience.cs");

        Assert.Contains("CompactStripLogicalHeight = 58", source, StringComparison.Ordinal);
        Assert.Contains("_monitorPane.RowStyles[0].SizeType = SizeType.Percent", source, StringComparison.Ordinal);
        Assert.Contains("_monitorPane.RowStyles[0].Height = 100F", source, StringComparison.Ordinal);
        Assert.Contains("_monitorPane.RowStyles[1].SizeType = SizeType.Absolute", source, StringComparison.Ordinal);
        Assert.Contains("Scale(_monitorPane, CompactStripLogicalHeight)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("68F", source, StringComparison.Ordinal);
        Assert.DoesNotContain("32F", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactStripReusesLiveSummaryAndOriginalEditButton()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactSelectedMonitorExperience.cs");

        Assert.Contains("button.Text, \"Edit Selected Monitor\"", source, StringComparison.Ordinal);
        Assert.Contains("_editor.SetCellPosition(_summaryValue, new TableLayoutPanelCellPosition(1, 0))", source, StringComparison.Ordinal);
        Assert.Contains("_editor.SetCellPosition(_editButton, new TableLayoutPanelCellPosition(3, 0))", source, StringComparison.Ordinal);
        Assert.Contains("_summaryValue.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("_editButton.Visible = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_editButton.Click +=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryEditorRowsAreRemovedFromPermanentVisualLayout()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactSelectedMonitorExperience.cs");

        Assert.Contains("_editor.RowStyles[1].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("_editor.RowStyles[2].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("_autoReplyLabel.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("_autoReplyValue.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("_enabledValue.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("_summaryLabel.Visible = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedStripReappliesOnDpiChangeWithoutBusinessMutation()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactSelectedMonitorExperience.cs");

        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("_monitorPane.DpiChangedAfterParent += _dpiChangedHandler", source, StringComparison.Ordinal);
        Assert.Contains("_dpiChangedHandler = (_, _) => Apply()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }
}
