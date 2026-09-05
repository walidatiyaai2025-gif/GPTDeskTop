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
    public void RateLimitNeverRotatesAndUncertainDeliveryNeverRetriesAutomatically()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");

        Assert.Contains("New Chat will NOT be used as a bypass", runner, StringComparison.Ordinal);
        Assert.Contains("send outcome is uncertain", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No rollover or automatic resend is allowed", runner, StringComparison.Ordinal);
        Assert.Contains("Fresh-chat rollover is blocked for this message", runner, StringComparison.Ordinal);
        Assert.Contains("WaitForRateLimitClearAsync", safety, StringComparison.Ordinal);
        Assert.Contains("No message will be sent or retried", safety, StringComparison.Ordinal);
        Assert.Contains("No physical send yet", safety, StringComparison.Ordinal);

        var senderCall = runner.IndexOf("session.Chrome.SendChatMessageVerifiedAsync(", StringComparison.Ordinal);
        var falseBranch = runner.IndexOf("if (!sent)", senderCall, StringComparison.Ordinal);
        Assert.True(senderCall >= 0 && falseBranch > senderCall);
        var afterFalse = runner[falseBranch..Math.Min(runner.Length, falseBranch + 1400)];
        Assert.DoesNotContain("RollOverBeforeSendAsync", afterFalse, StringComparison.Ordinal);
        Assert.DoesNotContain("continue;", afterFalse, StringComparison.Ordinal);
    }
}
