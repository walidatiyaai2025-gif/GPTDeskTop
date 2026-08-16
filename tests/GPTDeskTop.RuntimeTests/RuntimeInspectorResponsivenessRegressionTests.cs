namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeInspectorResponsivenessRegressionTests
{
    private static string ReadSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void BrowserResponsivenessProbeCannotBlockInspectorOnHungChromeWindow()
    {
        var source = ReadSource();

        Assert.Contains("BrowserWindowProbeTimeoutMs = 100", source, StringComparison.Ordinal);
        Assert.Contains("SmtoAbortIfHung", source, StringComparison.Ordinal);
        Assert.Contains("SendMessageTimeout(", source, StringComparison.Ordinal);
        Assert.Contains("IsWindowResponding(handle)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("process.Responding", source, StringComparison.Ordinal);
    }
}
