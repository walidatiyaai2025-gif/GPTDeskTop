namespace GPTDeskTop.Services;

public static class MonitorPollScheduler
{
    private const long MaxStaggerMilliseconds = 2_000;

    public static TimeSpan GetInitialStagger(long monitorId, TimeSpan pollPeriod)
    {
        if (monitorId <= 0 || pollPeriod <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var periodMilliseconds = Math.Max(1L, (long)Math.Round(pollPeriod.TotalMilliseconds));
        var phaseWindow = Math.Min(
            MaxStaggerMilliseconds,
            Math.Max(0L, periodMilliseconds * 4 / 5));
        if (phaseWindow < 2)
            return TimeSpan.Zero;

        var value = unchecked((ulong)monitorId);
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        value *= 0xc4ceb9fe1a85ec53UL;
        value ^= value >> 33;

        var staggerMilliseconds = (long)(value % (ulong)(phaseWindow + 1));
        return TimeSpan.FromMilliseconds(staggerMilliseconds);
    }
}
