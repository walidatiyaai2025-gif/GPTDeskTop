namespace GPTDeskTop.Services;
public static class ReleaseGate
{
    public static bool CanRelease(ReleaseReadinessReport report) => report.Ready && report.RequiredFailures == 0;
}
