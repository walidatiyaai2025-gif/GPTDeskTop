namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeHealthAutoRefreshRegressionTests
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
    public void RunningStateChangesRequestAFullHealthProbe()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");
        var handlerStart = source.IndexOf("private void OnRunningStateChanged()", StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        var handler = source[handlerStart..Math.Min(source.Length, handlerStart + 320)];
        Assert.Contains("UpdateRunningMonitorMetric();", handler, StringComparison.Ordinal);
        Assert.Contains("RequestRefresh();", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui(UpdateRunningMonitorMetric)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshRequestsAreCoalescedWhileAProbeIsRunning()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("private bool _refreshRequested;", source, StringComparison.Ordinal);
        Assert.Contains("private void RequestRefresh()", source, StringComparison.Ordinal);
        Assert.Contains("if (_loading)", source, StringComparison.Ordinal);
        Assert.Contains("_refreshRequested = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (_refreshRequested)", source, StringComparison.Ordinal);
        Assert.Contains("_refreshRequested = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (_loading || IsDisposed || Disposing) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticRefreshDoesNotAddRuntimeMutationOrPolling()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.DoesNotContain("System.Windows.Forms.Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveMonitorAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitorAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopMonitorAsync(", source, StringComparison.Ordinal);
    }
}
