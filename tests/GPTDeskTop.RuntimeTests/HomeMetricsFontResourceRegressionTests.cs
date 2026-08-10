namespace GPTDeskTop.RuntimeTests;

public sealed class HomeMetricsFontResourceRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void StatusCellFormattingReusesCachedFontInsteadOfAllocatingPerPaint()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "HomeMetricsService.cs"));

        var formattingStart = source.IndexOf("private void OnStatusCellFormatting", StringComparison.Ordinal);
        var cacheStart = source.IndexOf("private Font GetOrCreateStatusFont", StringComparison.Ordinal);

        Assert.True(formattingStart >= 0);
        Assert.True(cacheStart > formattingStart);

        var formattingBody = source[formattingStart..cacheStart];
        Assert.Contains(
            "style.Font = GetOrCreateStatusFont(grid.Font, presentation.FontStyle);",
            formattingBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new Font(", formattingBody, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusFontCacheUsesBaseFontIdentityAndIsDisposedWithService()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "HomeMetricsService.cs"));

        Assert.Contains("_statusFonts.TryGetValue(key, out var cached)", source, StringComparison.Ordinal);
        Assert.Contains("baseFont.FontFamily.Name", source, StringComparison.Ordinal);
        Assert.Contains("baseFont.Size", source, StringComparison.Ordinal);
        Assert.Contains("baseFont.Unit", source, StringComparison.Ordinal);
        Assert.Contains("baseFont.GdiCharSet", source, StringComparison.Ordinal);
        Assert.Contains("baseFont.GdiVerticalFont", source, StringComparison.Ordinal);
        Assert.Contains("_statusFonts.Add(key, created);", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var font in _statusFonts.Values) font.Dispose();", source, StringComparison.Ordinal);
        Assert.Contains("_statusFonts.Clear();", source, StringComparison.Ordinal);
    }
}