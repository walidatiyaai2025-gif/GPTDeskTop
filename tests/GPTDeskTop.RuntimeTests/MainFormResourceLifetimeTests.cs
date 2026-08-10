namespace GPTDeskTop.RuntimeTests;

public sealed class MainFormResourceLifetimeTests
{
    [Fact]
    public void MonitorServiceSubscriptionsUseNamedHandlersWithMatchingDisposeUnsubscriptions()
    {
        var source = ReadMainFormSource();
        var wire = Slice(source, "private void WireEvents()", "private void OnMonitorActivity");
        var dispose = Slice(source, "protected override void Dispose(bool disposing)", "private async Task CompleteShutdownAsync");

        Assert.Contains("_monitor.Activity += OnMonitorActivity;", wire, StringComparison.Ordinal);
        Assert.Contains("_monitor.HistoryChanged += OnMonitorHistoryChanged;", wire, StringComparison.Ordinal);
        Assert.Contains("_monitor.RunningStateChanged += OnMonitorRunningStateChanged;", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.Activity += (", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.HistoryChanged += ()", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.RunningStateChanged += ()", wire, StringComparison.Ordinal);

        Assert.Contains("if (disposing && !_uiResourcesDisposed)", dispose, StringComparison.Ordinal);
        Assert.Contains("_uiResourcesDisposed = true;", dispose, StringComparison.Ordinal);
        Assert.Contains("_monitor.Activity -= OnMonitorActivity;", dispose, StringComparison.Ordinal);
        Assert.Contains("_monitor.HistoryChanged -= OnMonitorHistoryChanged;", dispose, StringComparison.Ordinal);
        Assert.Contains("_monitor.RunningStateChanged -= OnMonitorRunningStateChanged;", dispose, StringComparison.Ordinal);
        Assert.Contains("base.Dispose(disposing);", dispose, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormOwnedUiResourcesAreDisposedExactlyFromDisposeLifecycle()
    {
        var source = ReadMainFormSource();
        var closing = Slice(source, "protected override void OnFormClosing", "protected override void Dispose(bool disposing)");
        var dispose = Slice(source, "protected override void Dispose(bool disposing)", "private async Task CompleteShutdownAsync");

        Assert.Contains("_monitorStatusFont.Dispose();", dispose, StringComparison.Ordinal);
        Assert.Contains("_toolTip.Dispose();", dispose, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorStatusFont.Dispose();", closing, StringComparison.Ordinal);
        Assert.DoesNotContain("_toolTip.Dispose();", closing, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, "_monitorStatusFont.Dispose();"));
        Assert.Equal(1, CountOccurrences(source, "_toolTip.Dispose();"));
    }

    [Fact]
    public void NamedMonitorHandlersPreserveExistingUiRefreshBehaviorAndDisposalGuard()
    {
        var source = ReadMainFormSource();
        var handlers = Slice(source, "private void OnMonitorActivity", "private void ConfigureTooltips()");
        var ui = Slice(source, "private void Ui(Action action)", "private static string GetAppVersion()");

        Assert.Contains("AppendActivity($\"M{id}: {message}\")", handlers, StringComparison.Ordinal);
        Assert.Contains("RefreshHistoryAsync()", handlers, StringComparison.Ordinal);
        Assert.Contains("RefreshMonitorsAsync()", handlers, StringComparison.Ordinal);
        Assert.Contains("UpdateActionStates();", handlers, StringComparison.Ordinal);
        Assert.Contains("_shutdownRequested || IsDisposed || Disposing", ui, StringComparison.Ordinal);
    }

    private static string ReadMainFormSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainForm.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
