namespace GPTDeskTop.RuntimeTests;

public sealed class ShutdownFinalizationRegressionTests
{
    private static string ReadMainForm()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainForm.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void FinalizingClosesSynchronouslyInsteadOfPostingAnotherClose()
    {
        var source = ReadMainForm();
        var finalizing = source.IndexOf("_shutdownOverlay.SetStatus(\"Finalizing…\")", StringComparison.Ordinal);
        Assert.True(finalizing >= 0);
        var tail = source[finalizing..];

        Assert.Contains("_shutdownCompleted = true", tail, StringComparison.Ordinal);
        Assert.Contains("if (InvokeRequired)", tail, StringComparison.Ordinal);
        Assert.Contains("Invoke(new Action(Close))", tail, StringComparison.Ordinal);
        Assert.Contains("Close();", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInvoke(new Action(Close))", tail, StringComparison.Ordinal);
    }

    [Fact]
    public void ShutdownHasAProcessLevelBoundAndDisposesItsWatchdog()
    {
        var source = ReadMainForm();

        Assert.Contains("_shutdownHardExitWatchdog ??= new System.Threading.Timer", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(20)", source, StringComparison.Ordinal);
        Assert.Contains("_ => Environment.Exit(0)", source, StringComparison.Ordinal);
        Assert.Contains("_shutdownHardExitWatchdog?.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("Application.ExitThread()", source, StringComparison.Ordinal);
    }
}
