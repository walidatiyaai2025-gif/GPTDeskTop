namespace GPTDeskTop.Services;
public enum DashboardAge { Fresh, Aging, Stale }
public static class DashboardAgePolicy
{
    public static DashboardAge Get(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var minutes = (now - updatedAt).TotalMinutes;
        if (minutes < 2) return DashboardAge.Fresh;
        if (minutes < 10) return DashboardAge.Aging;
        return DashboardAge.Stale;
    }
}
