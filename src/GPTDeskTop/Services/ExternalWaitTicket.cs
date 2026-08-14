namespace GPTDeskTop.Services;
public sealed record ExternalWaitTicket(string ProjectId, string TaskId, string DependencyType, string DependencyId, DateTimeOffset StartedAt, DateTimeOffset? Deadline, string ResumeCondition);
