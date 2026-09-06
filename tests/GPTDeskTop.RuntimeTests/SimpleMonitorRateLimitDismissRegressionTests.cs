namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorRateLimitDismissRegressionTests
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
    public void VisibleRateLimitNoticeIsDismissedAutomatically()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("const dismissControl = root =>", source, StringComparison.Ordinal);
        Assert.Contains("button.click();", source, StringComparison.Ordinal);
        Assert.Contains("dismissControl(element);", source, StringComparison.Ordinal);
        Assert.Contains("dismissControl(root);", source, StringComparison.Ordinal);
        Assert.Contains("got it|ok|okay|dismiss|close|understood", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("element.closest(transcriptSelector)", source, StringComparison.Ordinal);
        Assert.Contains("pattern.test(text)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DismissalDoesNotBypassDurableRateLimitCooldown()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("await ActivateRateLimitAsync(rateLimitText", source, StringComparison.Ordinal);
        Assert.Contains("WaitForRateLimitClearAsync", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(5)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(10)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(15)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(30)", source, StringComparison.Ordinal);
        Assert.Contains("No message will be sent or retried", source, StringComparison.Ordinal);

        var dismissStart = source.IndexOf("const dismissControl = root =>", StringComparison.Ordinal);
        var dismissEnd = source.IndexOf("const hasDismissControl = root =>", dismissStart, StringComparison.Ordinal);
        Assert.True(dismissStart >= 0 && dismissEnd > dismissStart);
        var dismissBlock = source[dismissStart..dismissEnd];
        Assert.DoesNotContain("ClearRateLimitAsync", dismissBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalSendGate.Release", dismissBlock, StringComparison.Ordinal);
    }
}
