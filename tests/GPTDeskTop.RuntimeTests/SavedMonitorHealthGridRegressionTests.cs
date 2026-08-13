namespace GPTDeskTop.RuntimeTests;

public sealed class SavedMonitorHealthGridRegressionTests
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
    public void SavedMonitorGridAddsLiveReasonAndWholeRowHealthColors()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SavedMonitorHealthGridExperience.cs");

        Assert.Contains("HeaderText = \"Reason\"", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.DangerSubtle", source, StringComparison.Ordinal);
        Assert.Contains("row.DefaultCellStyle.BackColor", source, StringComparison.Ordinal);
        Assert.Contains("row.DefaultCellStyle.SelectionBackColor", source, StringComparison.Ordinal);
        Assert.Contains("health.Reason", source, StringComparison.Ordinal);
        Assert.Contains("health.Status", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthIsContinuouslyVerifiedWithoutTreatingSlowChatAsFailure()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SavedMonitorHealthGridExperience.cs");
        var presentation = ReadSource("src", "GPTDeskTop", "Services", "SavedMonitorHealthPresentation.cs");

        Assert.Contains("HealthScanIntervalMs = 2500", source, StringComparison.Ordinal);
        Assert.Contains("_chrome.GetTabsAsync", source, StringComparison.Ordinal);
        Assert.Contains("_chrome.GetChatStateAsync", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref _scanInProgress", source, StringComparison.Ordinal);
        Assert.Contains("_form.FormClosing += OnFormClosing", source, StringComparison.Ordinal);
        Assert.Contains("pageState.IsGenerating", presentation, StringComparison.Ordinal);
        Assert.Contains("Monitoring normally — ChatGPT is generating a response.", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void GridCapturesFailureReasonAndCanClearItAfterRecovery()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SavedMonitorHealthGridExperience.cs");

        Assert.Contains("_monitor.Activity += OnMonitorActivity", source, StringComparison.Ordinal);
        Assert.Contains("_recentFailures[monitorId]", source, StringComparison.Ordinal);
        Assert.Contains("_recentFailures.TryRemove", source, StringComparison.Ordinal);
        Assert.Contains("LooksLikeFailure", source, StringComparison.Ordinal);
        Assert.Contains("LooksLikeHealthyTransition", source, StringComparison.Ordinal);
        Assert.Contains("RunningStateChanged += OnRunningStateChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CellFormattingDoesNotAllocateFontsPerPaint()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SavedMonitorHealthGridExperience.cs");
        var formattingStart = source.IndexOf("private void OnCellFormatting", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private static bool LooksLikeFailure", formattingStart, StringComparison.Ordinal);
        var formatting = source[formattingStart..nextMethod];

        Assert.DoesNotContain("new Font", formatting, StringComparison.Ordinal);
    }
}
