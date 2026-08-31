namespace GPTDeskTop.RuntimeTests;

public sealed class StrictWallClockSendGapRegressionTests
{
    [Fact]
    public void ProductionCooldownClosesAnyEarlyTimerWakeBeforeGlobalRelease()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs")));

        Assert.Contains("_usesSystemDelay = delayAsync is null", source, StringComparison.Ordinal);
        Assert.Contains("while (DateTimeOffset.UtcNow < nextSendUtc.Value)", source, StringComparison.Ordinal);
        Assert.Contains("remaining + TimeSpan.FromMilliseconds(1)", source, StringComparison.Ordinal);
        Assert.Contains("DefaultInterSendGap = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
    }
}
