using GPTDeskTop.Services;

namespace GPTDeskTop.Runtime;

/// <summary>
/// Binds the durable autonomous state machine to the existing saved-monitor lifecycle.
/// A saved monitor is the current production continuation entrypoint; completion remains
/// VerifyingCompletion until an external acceptance producer supplies all required gates.
/// </summary>
public sealed class MonitorAutonomousTaskIntegration
{
    private readonly string _directory;
    private readonly object _sync = new();
    private readonly Dictionary<long, AutonomousTaskController> _controllers = new();

    public MonitorAutonomousTaskIntegration(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "autonomous-tasks");
    }

    public AutonomousTaskCheckpoint StartOrRestore(long monitorId, string conversationKey)
        => Controller(monitorId, conversationKey).Snapshot;

    public void Transition(long monitorId, string conversationKey, AutonomousTaskPhase phase, string reason)
        => Safely(() => Controller(monitorId, conversationKey).Transition(phase, reason), monitorId);

    public void Rollover(long monitorId, string conversationKey)
        => Safely(() => Controller(monitorId, conversationKey).Rollover(conversationKey), monitorId);

    public AutonomousTaskCheckpoint? Snapshot(long monitorId)
    {
        lock (_sync) return _controllers.TryGetValue(monitorId, out var controller) ? controller.Snapshot : null;
    }

    private AutonomousTaskController Controller(long monitorId, string conversationKey)
    {
        lock (_sync)
        {
            if (_controllers.TryGetValue(monitorId, out var existing)) return existing;
            var path = Path.Combine(_directory, $"monitor-{monitorId}.json");
            return _controllers[monitorId] = new AutonomousTaskController(path, $"monitor:{monitorId}", conversationKey);
        }
    }

    private static void Safely(Action action, long monitorId)
    {
        try { action(); }
        catch (Exception ex) { ExceptionLogService.Log(ex, $"MonitorAutonomousTaskIntegration.Monitor{monitorId}"); }
    }
}
