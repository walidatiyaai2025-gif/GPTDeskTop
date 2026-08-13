namespace GPTDeskTop.RuntimeTests;

public sealed class MainFooterLayoutRegressionTests
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
    public void V2FooterOwnsAFontAwareAbsoluteRowInsteadOfTheLegacy24PixelStrip()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var footerStart = source.IndexOf("private static Label BuildFooter", StringComparison.Ordinal);
        var summaryStart = source.IndexOf("private static string BuildDevelopmentFooterText", footerStart, StringComparison.Ordinal);
        Assert.True(footerStart >= 0 && summaryStart > footerStart);

        var footer = source[footerStart..summaryStart];
        Assert.Contains("var footerHeight = Math.Max", footer, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[4].SizeType = SizeType.Absolute", footer, StringComparison.Ordinal);
        Assert.Contains("root.RowStyles[4].Height = footerHeight", footer, StringComparison.Ordinal);
        Assert.Contains("MinimumSize = new Size(0, (int)Math.Ceiling(footerHeight))", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void FooterTextRegionsCannotPaintAcrossTheirCellsWhenWidthIsConstrained()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var footerStart = source.IndexOf("private static Label BuildFooter", StringComparison.Ordinal);
        var summaryStart = source.IndexOf("private static string BuildDevelopmentFooterText", footerStart, StringComparison.Ordinal);
        var footer = source[footerStart..summaryStart];

        Assert.Contains("footerStatus", footer, StringComparison.Ordinal);
        Assert.Contains("AutoEllipsis = true", footer, StringComparison.Ordinal);
        Assert.Contains("versionLabel.AutoEllipsis = true", footer, StringComparison.Ordinal);
        Assert.Contains("footerStatus.Font.Height", footer, StringComparison.Ordinal);
        Assert.Contains("versionLabel.Font.Height", footer, StringComparison.Ordinal);
        Assert.Contains("new ColumnStyle(SizeType.Percent, 24F)", footer, StringComparison.Ordinal);
        Assert.Contains("new ColumnStyle(SizeType.Percent, 52F)", footer, StringComparison.Ordinal);
        Assert.Equal(2, footer.Split("new ColumnStyle(SizeType.Percent, 24F)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void FooterRemovesDefaultControlMarginsAndKeepsReadableInternalPadding()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OperatorWorkspaceV2Experience.cs");
        var footerStart = source.IndexOf("private static Label BuildFooter", StringComparison.Ordinal);
        var summaryStart = source.IndexOf("private static string BuildDevelopmentFooterText", footerStart, StringComparison.Ordinal);
        var footer = source[footerStart..summaryStart];

        Assert.Contains("Margin = Padding.Empty", footer, StringComparison.Ordinal);
        Assert.Contains("footerStatus", footer, StringComparison.Ordinal);
        Assert.Contains("Padding = new Padding(8, 0, 8, 0)", footer, StringComparison.Ordinal);
        Assert.Contains("versionLabel.Margin = Padding.Empty", footer, StringComparison.Ordinal);
        Assert.Contains("versionLabel.Padding = new Padding(8, 0, 0, 0)", footer, StringComparison.Ordinal);
    }
}
