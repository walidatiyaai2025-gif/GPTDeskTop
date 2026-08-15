using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorPollSchedulerTests
{
    [Fact]
    public void OneSecondMonitorsAreDistributedWithoutChangingPeriodBound()
    {
        var delays = Enumerable.Range(1, 64)
            .Select(id => MonitorPollScheduler.GetInitialStagger(id, TimeSpan.FromSeconds(1)))
            .ToArray();

        Assert.All(delays, delay => Assert.InRange(delay.TotalMilliseconds, 0, 800));
        Assert.True(delays.Distinct().Count() >= 48);
        Assert.Equal(
            MonitorPollScheduler.GetInitialStagger(17, TimeSpan.FromSeconds(1)),
            MonitorPollScheduler.GetInitialStagger(17, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void LongerPeriodsUseAtMostTwoSecondPhaseWindow()
    {
        var delays = Enumerable.Range(1, 64)
            .Select(id => MonitorPollScheduler.GetInitialStagger(id, TimeSpan.FromSeconds(30)))
            .ToArray();

        Assert.All(delays, delay => Assert.InRange(delay.TotalMilliseconds, 0, 2000));
        Assert.True(delays.Distinct().Count() >= 60);
    }

    [Fact]
    public void InvalidMonitorOrPeriodDoesNotDelay()
    {
        Assert.Equal(TimeSpan.Zero, MonitorPollScheduler.GetInitialStagger(0, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.Zero, MonitorPollScheduler.GetInitialStagger(1, TimeSpan.Zero));
    }

    [Fact]
    public void MonitorLoopTakesInitialSnapshotBeforeApplyingRepeatingPollStagger()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var source = File.ReadAllText(path);

        // The initial state is intentionally declared separately so the flight-recorder scope can
        // wrap the first CDP read. Assert behavior/order rather than the local declaration syntax.
        var initialSnapshot = source.IndexOf("initial = await GetChatStateWithRetryAsync", StringComparison.Ordinal);
        Assert.True(initialSnapshot >= 0);

        var stagger = source.IndexOf("MonitorPollScheduler.GetInitialStagger", initialSnapshot, StringComparison.Ordinal);
        var delay = source.IndexOf("await Task.Delay(initialPollStagger, cancellationToken);", stagger, StringComparison.Ordinal);
        var timer = source.IndexOf("new PeriodicTimer(pollPeriod)", delay, StringComparison.Ordinal);

        Assert.True(stagger > initialSnapshot);
        Assert.True(delay > stagger);
        Assert.True(timer > delay);
    }
}
