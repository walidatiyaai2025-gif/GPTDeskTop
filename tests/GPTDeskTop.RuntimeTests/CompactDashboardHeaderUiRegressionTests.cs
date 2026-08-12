namespace GPTDeskTop.RuntimeTests;

public sealed class CompactDashboardHeaderUiRegressionTests
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
    public void CompactHeaderRunsAfterDashboardMakeoverAndOwnsFinalHeight()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactDashboardHeaderLayout.cs");

        Assert.Contains("MainDashboardExperience.Apply(form);", source, StringComparison.Ordinal);
        Assert.Contains("private const int HeaderLogicalHeight = 44;", source, StringComparison.Ordinal);
        Assert.Contains("parts.Root.RowStyles[0].SizeType = SizeType.Absolute;", source, StringComparison.Ordinal);
        Assert.Contains("parts.Root.RowStyles[0].Height = Scale(parts.Root, HeaderLogicalHeight);", source, StringComparison.Ordinal);
        Assert.Contains("form.DpiChanged += (_, _) => ApplyPhysicalLayout(registration);", source, StringComparison.Ordinal);
        Assert.Contains("Single physical-layout owner for the compact main-dashboard status header", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleConsumesNoVisualRowButPurposeRemainsAccessible()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactDashboardHeaderLayout.cs");

        Assert.Contains("private const string HeaderGuidance = \"ChatGPT monitoring, recovery and conversation automation\";", source, StringComparison.Ordinal);
        Assert.Contains("parts.TitleBlock.RowStyles[1].SizeType = SizeType.Absolute;", source, StringComparison.Ordinal);
        Assert.Contains("parts.TitleBlock.RowStyles[1].Height = 0;", source, StringComparison.Ordinal);
        Assert.Contains("parts.Subtitle.Visible = false;", source, StringComparison.Ordinal);
        Assert.Contains("parts.Header.AccessibleDescription = HeaderGuidance", source, StringComparison.Ordinal);
        Assert.Contains("parts.Title.AccessibleDescription = HeaderGuidance", source, StringComparison.Ordinal);
        Assert.Contains("SetToolTip(parts.Title, HeaderGuidance", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FourMetricChipsUseSingleRowThirtyPixelPresentationWithoutReplacingBoundLabels()
    {
        var compact = ReadSource("src", "GPTDeskTop", "UI", "CompactDashboardHeaderLayout.cs");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("private const int MetricLogicalWidth = 108;", compact, StringComparison.Ordinal);
        Assert.Contains("private const int MetricLogicalHeight = 30;", compact, StringComparison.Ordinal);
        Assert.Contains("if (chipPanels.Length != 4)", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Layout.RowCount = 1;", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Layout.ColumnCount = 2;", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Layout.Controls.Add(metric.Caption, 0, 0);", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Layout.Controls.Add(metric.Value, 1, 0);", compact, StringComparison.Ordinal);
        Assert.Contains("Preserve the exact Label instances that MainForm owns/binds", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Panel.Height = Scale(metric.Panel, MetricLogicalHeight);", compact, StringComparison.Ordinal);
        Assert.Contains("metric.Panel.MaximumSize = new Size(Scale(metric.Panel, MetricLogicalWidth), Scale(metric.Panel, MetricLogicalHeight));", compact, StringComparison.Ordinal);

        Assert.Contains("CreateMetricChip(\"Running\", _runningMetricValue)", main, StringComparison.Ordinal);
        Assert.Contains("CreateMetricChip(\"Monitors\", _monitorsMetricValue)", main, StringComparison.Ordinal);
        Assert.Contains("CreateMetricChip(\"Conversation tabs\", _tabsMetricValue)", main, StringComparison.Ordinal);
        Assert.Contains("CreateMetricChip(\"Chrome window\", _chromeMetricValue)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderReturnsAdditionalPaddingSpaceToWorkspace()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactDashboardHeaderLayout.cs");

        Assert.Contains("private const int RootVerticalPadding = 8;", source, StringComparison.Ordinal);
        Assert.Contains("private const int HeaderVerticalPadding = 2;", source, StringComparison.Ordinal);
        Assert.Contains("private const int HeaderBottomMargin = 3;", source, StringComparison.Ordinal);
        Assert.Contains("parts.Root.Padding = new Padding(rootHorizontal, rootVertical, rootHorizontal, rootVertical);", source, StringComparison.Ordinal);
        Assert.Contains("parts.Header.Padding = new Padding(", source, StringComparison.Ordinal);
        Assert.Contains("parts.Header.Margin = new Padding(0, 0, 0, Scale(parts.Header, HeaderBottomMargin));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricCaptionGuidanceRemainsAccessibleWhenSpaceIsTight()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactDashboardHeaderLayout.cs");

        Assert.Contains("metric.Caption.AccessibleName = metric.Caption.Text;", source, StringComparison.Ordinal);
        Assert.Contains("metric.Value.AccessibleDescription = $\"Current {metric.Caption.Text} value\";", source, StringComparison.Ordinal);
        Assert.Contains("SetToolTip(metric.Caption, metric.Caption.Text);", source, StringComparison.Ordinal);
        Assert.Contains("metric.Caption.AutoEllipsis = true;", source, StringComparison.Ordinal);
        Assert.Contains("metric.Value.AutoEllipsis = true;", source, StringComparison.Ordinal);
    }
}
