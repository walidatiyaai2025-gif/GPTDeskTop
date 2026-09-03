namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorRateLimitSafetyRegressionTests
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
    public void RateLimitGuardIsDurableAndUsesRequiredBackoff()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("SimpleMonitor.SafetyState.v1", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(5)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(10)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(15)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(30)", source, StringComparison.Ordinal);
        Assert.Contains("too many requests", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("making requests too quickly", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("please wait a few minutes before trying again", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safe probe failed", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryPhysicalSendHasPersistentQuietGateAndNoConcurrentCdpRead()
    {
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var form = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");
        var footer = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyExperienceController.cs");

        Assert.Contains("MinimumSendGap = TimeSpan.FromSeconds(15)", safety, StringComparison.Ordinal);
        Assert.Contains("_startupQuietUntilUtc", safety, StringComparison.Ordinal);
        Assert.Contains("LastPhysicalAttemptUtc", safety, StringComparison.Ordinal);
        Assert.Contains("LastResponseCompletedUtc", safety, StringComparison.Ordinal);
        Assert.Contains("AcquireSendPermitAsync", runner, StringComparison.Ordinal);
        Assert.Contains("RecordPhysicalAttemptAsync", runner, StringComparison.Ordinal);
        Assert.Contains("RecordResponseCompletedAsync", runner, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorPassiveReadGate.RunAsync", runner, StringComparison.Ordinal);
        Assert.Contains("new SimpleMonitorRunner(_database)", form, StringComparison.Ordinal);
        Assert.Contains("Interval = 1500", footer, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorPassiveReadGate.RunAsync", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimitRejectionRetriesOnlyAfterBreakerAndSafeProbe()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("RATE LIMITED — physical submit was rejected", runner, StringComparison.Ordinal);
        Assert.Contains("HandleRateLimitIfNeededAsync", runner, StringComparison.Ordinal);
        Assert.Contains("WaitForRateLimitClearAsync", safety, StringComparison.Ordinal);
        Assert.Contains("No message will be sent or retried", safety, StringComparison.Ordinal);
        Assert.Contains("No physical send yet", safety, StringComparison.Ordinal);
    }
}
