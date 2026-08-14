namespace GPTDeskTop.Services;

/// <summary>
/// Short-lived handoff between the Projects Hub wizard/preflight and the existing new-chat monitor
/// workflow. UXHUB-021..025 consume and clear this validated draft when the bootstrap message and
/// project state are created. It never stores the GitHub token.
/// </summary>
public static class NewProjectMonitorPendingContext
{
    private static readonly object Sync = new();
    private static NewProjectMonitorDraft? _current;

    public static void Set(NewProjectMonitorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        lock (Sync) _current = draft;
    }

    public static NewProjectMonitorDraft? Peek()
    {
        lock (Sync) return _current;
    }

    public static NewProjectMonitorDraft? Take()
    {
        lock (Sync)
        {
            var current = _current;
            _current = null;
            return current;
        }
    }

    public static void Clear()
    {
        lock (Sync) _current = null;
    }
}
