namespace GPTDeskTop.Services;
public static class ExternalWaitPolicy
{
    public static bool ShouldPoll(ExternalWaitStatus status, DateTimeOffset now, DateTimeOffset nextCheckAt, DateTimeOffset deadline) => status == ExternalWaitStatus.Pending && now >= nextCheckAt && now < deadline;
}
