namespace GPTDeskTop.RuntimeTests;

public sealed class MainFormResourceLifecycleRegressionTests
{
    private static string ReadMainFormSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainForm.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void MonitorServiceSubscriptionsUseNamedHandlersWithMatchingUnsubscriptions()
    {
        var source = ReadMainFormSource();

        Assert.Contains("_monitor.Activity += OnMonitorActivity;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.HistoryChanged += OnMonitorHistoryChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.RunningStateChanged += OnMonitorRunningStateChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.Activity -= OnMonitorActivity;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.HistoryChanged -= OnMonitorHistoryChanged;", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.RunningStateChanged -= OnMonitorRunningStateChanged;", source, StringComparison.Ordinal);

        Assert.DoesNotContain("_monitor.Activity += (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.HistoryChanged += ()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.RunningStateChanged += ()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormOwnedResourcesAreDisposedExactlyOnceThroughDisposeLifecycle()
    {
        var source = ReadMainFormSource();
        var disposeStart = source.IndexOf("protected override void Dispose(bool disposing)", StringComparison.Ordinal);
        var closingStart = source.IndexOf("protected override void OnFormClosing", StringComparison.Ordinal);

        Assert.True(disposeStart >= 0);
        Assert.True(closingStart > disposeStart);

        var disposeBody = source[disposeStart..closingStart];
        Assert.Contains("if (disposing && !_ownedResourcesDisposed)", disposeBody, StringComparison.Ordinal);
        Assert.Contains("_ownedResourcesDisposed = true;", disposeBody, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(disposeBody, "_toolTip.Dispose();"));
        Assert.Equal(1, CountOccurrences(disposeBody, "_monitorStatusFont.Dispose();"));

        var closingBody = source[closingStart..];
        Assert.DoesNotContain("_toolTip.Dispose();", closingBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorStatusFont.Dispose();", closingBody, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
