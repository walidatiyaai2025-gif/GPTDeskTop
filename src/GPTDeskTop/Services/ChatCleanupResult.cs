namespace GPTDeskTop.Services;
public enum ChatCleanupResult { NotAttempted, Deleted, AlreadyAbsent, Deferred, Failed }
public sealed record ChatCleanupOutcome(ChatCleanupResult Result, string Message, DateTimeOffset At);
