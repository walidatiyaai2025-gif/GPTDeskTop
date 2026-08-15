namespace GPTDeskTop.Services;

/// <summary>
/// Sanitized runtime evidence for the verified-send state machine. This intentionally records
/// no prompt, message, conversation title, URL, or browser target identifier.
/// </summary>
public static class VerifiedSendDiagnostics
{
    private static readonly object Sync = new();
    private static VerifiedSendDiagnosticSnapshot _last = new(
        Phase: "NotObserved",
        Reason: "not-observed",
        SubmitAttempts: 0,
        ObservedAtUtc: DateTimeOffset.MinValue);

    public static VerifiedSendDiagnosticSnapshot Last
    {
        get
        {
            lock (Sync)
                return _last;
        }
    }

    internal static void Record(string phase, string reason, int submitAttempts)
    {
        lock (Sync)
            _last = new VerifiedSendDiagnosticSnapshot(
                phase,
                reason,
                Math.Max(0, submitAttempts),
                DateTimeOffset.UtcNow);
    }
}

public sealed record VerifiedSendDiagnosticSnapshot(
    string Phase,
    string Reason,
    int SubmitAttempts,
    DateTimeOffset ObservedAtUtc);
