namespace GPTDeskTop.Services;
public sealed record DashboardRefreshRequest(string ProjectId, string Cause, DateTimeOffset RequestedAt, bool Force);
