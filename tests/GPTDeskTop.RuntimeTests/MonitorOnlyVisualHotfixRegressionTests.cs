namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyVisualHotfixRegressionTests
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
    public void RuntimeActionsHaveADedicatedAlwaysVisibleBar()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyVisualHotfix.cs");

        Assert.Contains("MonitorOnlyRuntimeActionBar", source, StringComparison.Ordinal);
        Assert.Contains("Dock = DockStyle.Bottom", source, StringComparison.Ordinal);
        Assert.Contains("actions.Controls.Add(start, 0, 0)", source, StringComparison.Ordinal);
        Assert.Contains("actions.Controls.Add(stop, 1, 0)", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.StyleButton(start, primary: true)", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.StyleButton(stop, danger: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FooterCannotBePushedBelowViewportAndShowsVersion()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyVisualHotfix.cs");

        Assert.Contains("root.AutoScroll = false", source, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[3] = new RowStyle(SizeType.Absolute, 60)", source, StringComparison.Ordinal);
        Assert.Contains("MonitorOnlyVersionLabel", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"GPTDeskTop version\"", source, StringComparison.Ordinal);
        Assert.Contains("form.Text = $\"GPTDeskTop v{GetProductVersion()} — Monitor Only\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductVersionIsBumpedToTwoPointZeroPointTwentyFour()
    {
        var props = ReadSource("Directory.Build.props");
        Assert.Contains("<GPTDeskTopVersion>2.0.24</GPTDeskTopVersion>", props, StringComparison.Ordinal);
    }
}
