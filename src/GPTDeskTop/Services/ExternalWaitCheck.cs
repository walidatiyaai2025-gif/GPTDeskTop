namespace GPTDeskTop.Services;
public sealed record ExternalWaitCheck(string ProjectId, string TaskId, string DependencyType, string DependencyId, int CheckNumber, DateTimeOffset CheckedAt, ExternalWaitStatus Status, string Detail);
