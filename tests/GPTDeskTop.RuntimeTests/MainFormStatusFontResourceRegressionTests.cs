namespace GPTDeskTop.RuntimeTests;

public sealed class MainFormStatusFontResourceRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void RuntimeStatusFormattingReusesOwnedFontInsteadOfAllocatingPerPaint()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "UI", "MainForm.cs"));

        var formattingStart = source.IndexOf("private void FormatMonitorCell", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private void FormatHistoryCell", StringComparison.Ordinal);

        Assert.True(formattingStart >= 0);
        Assert.True(nextMethod > formattingStart);

        var formattingBody = source[formattingStart..nextMethod];
        Assert.Contains("style.Font = _monitorStatusFont;", formattingBody, StringComparison.Ordinal);
        Assert.DoesNotContain("new Font(", formattingBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStatusFontIsOwnedAndDisposedByMainFormLifecycle()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "UI", "MainForm.cs"));

        Assert.Contains(
            "private readonly Font _monitorStatusFont = new(\"Segoe UI Variable Text\", 9F, FontStyle.Bold);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_monitorStatusFont.Dispose();", source, StringComparison.Ordinal);
        Assert.Contains("if (_shutdownCompleted)", source, StringComparison.Ordinal);
    }
}
