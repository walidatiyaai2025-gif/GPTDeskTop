namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorRateLimitOverlayRegressionTests
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
    public void CurrentChatGptProtectionOverlayIsDetectedWithoutDialogAria()
    {
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("too many requests", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("making requests too quickly", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporarily limited access to your conversations", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("please wait a few minutes before trying again", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("got it", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-state=\"open\"", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-radix-portal", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("modalRoot", safety, StringComparison.Ordinal);
        Assert.Contains("hasDismissControl", safety, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptTextAloneCannotTripTheRateLimitBreaker()
    {
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("transcriptSelector", safety, StringComparison.Ordinal);
        Assert.Contains("element.closest(transcriptSelector)", safety, StringComparison.Ordinal);
        Assert.Contains("require modal evidence", safety, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("return pattern.test(document.body", safety, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseIdentityIsVersion2028()
    {
        var props = ReadSource("Directory.Build.props");
        Assert.Contains("<GPTDeskTopVersion>2.0.28</GPTDeskTopVersion>", props, StringComparison.Ordinal);
    }
}
