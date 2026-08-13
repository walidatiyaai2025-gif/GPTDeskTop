namespace GPTDeskTop.Services;
public static class DashboardRefreshPolicy
{
    public static bool ShouldRefresh(DashboardAge age, bool projectChanged, bool userRequested) => userRequested || projectChanged || age != DashboardAge.Fresh;
}
