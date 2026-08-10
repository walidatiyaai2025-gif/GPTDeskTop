namespace GPTDeskTop.RuntimeTests;

public sealed class UiResourceLifecycleBatchRegressionTests
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
    public void HistoryStatusFormattingReusesOwnedFontAndDisposesIt()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "HistoryWorkspaceControl.cs");
        var formattingStart = source.IndexOf("private void FormatStatusCell", StringComparison.Ordinal);
        var processKeysStart = source.IndexOf("protected override bool ProcessCmdKey", StringComparison.Ordinal);

        Assert.True(formattingStart >= 0);
        Assert.True(processKeysStart > formattingStart);
        var formattingBody = source[formattingStart..processKeysStart];
        Assert.Contains("style.Font = _statusFont;", formattingBody, StringComparison.Ordinal);
        Assert.DoesNotContain("new Font(", formattingBody, StringComparison.Ordinal);
        Assert.Contains("private readonly Font _statusFont = new", source, StringComparison.Ordinal);
        Assert.Contains("_statusFont.Dispose();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryRefreshIsBoundToControlLifetime()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "HistoryWorkspaceControl.cs");

        Assert.Contains("private readonly CancellationTokenSource _lifetimeCancellation = new();", source, StringComparison.Ordinal);
        Assert.Contains("GetRecentLogsAsync(500, _lifetimeCancellation.Token)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Dispose();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentDashboardTimerRunsOnlyForVisibleLiveCountdowns()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "DevelopmentTaskDashboardControl.cs");
        var constructorStart = source.IndexOf("public DevelopmentTaskDashboardControl", StringComparison.Ordinal);
        var buildUiStart = source.IndexOf("private void BuildUi", StringComparison.Ordinal);

        Assert.True(constructorStart >= 0);
        Assert.True(buildUiStart > constructorStart);
        Assert.DoesNotContain("_timer.Start();", source[constructorStart..buildUiStart], StringComparison.Ordinal);
        Assert.Contains("VisibleChanged += (_, _) => Render();", source, StringComparison.Ordinal);
        Assert.Contains("UpdateTimerState(state.Status);", source, StringComparison.Ordinal);
        Assert.Contains("var shouldRun = Visible", source, StringComparison.Ordinal);
        Assert.Contains("&& _expanded", source, StringComparison.Ordinal);
        Assert.Contains("DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling", source, StringComparison.Ordinal);
        Assert.Contains("if (!_timer.Enabled) _timer.Start();", source, StringComparison.Ordinal);
        Assert.Contains("else if (_timer.Enabled)", source, StringComparison.Ordinal);
        Assert.Contains("_timer.Stop();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorRepairDiscoveryUsesLinkedLifetimeCancellation()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs");

        Assert.Contains("private readonly CancellationTokenSource _lifetimeCancellation = new();", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token)", source, StringComparison.Ordinal);
        Assert.Contains("timeout.CancelAfter(TimeSpan.FromSeconds(5));", source, StringComparison.Ordinal);
        Assert.Contains("GetSavedMonitorsAsync(timeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("GetTabsAsync(timeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Dispose();", source, StringComparison.Ordinal);
    }
}
