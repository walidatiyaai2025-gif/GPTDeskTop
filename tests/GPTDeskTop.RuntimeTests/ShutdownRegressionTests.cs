namespace GPTDeskTop.RuntimeTests;

public sealed class ShutdownRegressionTests
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
    public void MainFormCloseIsAsyncIdempotentAndBounded()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("private bool _shutdownRequested;", source, StringComparison.Ordinal);
        Assert.Contains("private bool _shutdownCompleted;", source, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", source, StringComparison.Ordinal);
        Assert.Contains("CompleteShutdownAsync", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(10)", source, StringComparison.Ordinal);
        Assert.Contains("_monitor.StopAllAsync().WaitAsync(timeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("_chrome.CloseAllMonitorTabsAsync(timeout.Token)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitor.StopAllAsync().GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_chrome.CloseAllMonitorTabsAsync().GetAwaiter().GetResult()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeEventsAreIgnoredOnceShutdownBegins()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("if (_shutdownRequested || IsDisposed || Disposing) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalCleanupRunsAfterWinFormsMessageLoopAndOffTheUiContext()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var runIndex = source.IndexOf("Application.Run(mainForm);", StringComparison.Ordinal);
        var finalizeIndex = source.IndexOf("Task.Run(() => FinalizeGracefulShutdownAsync(database, developmentRuntime)).GetAwaiter().GetResult();", StringComparison.Ordinal);

        Assert.True(runIndex >= 0);
        Assert.True(finalizeIndex > runIndex);
        Assert.Contains(".ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(TimeSpan.FromSeconds(5))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mainForm.FormClosed +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashRecoveryStateService.MarkCleanShutdownAsync(database).GetAwaiter().GetResult()", source, StringComparison.Ordinal);
    }
}