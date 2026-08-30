using System.Text.Json;

namespace GPTDeskTop.Runtime;

public enum AutonomousTaskPhase
{
    TaskPending, TaskRunning, WaitingForChatGpt, WaitingForSafeSend, WaitingInGlobalQueue,
    Recovering, RateLimitPaused, ConversationRollover, VerifyingCompletion, Completed,
    FailedTerminal, HumanActionRequired
}

public sealed record AutonomousAcceptanceGates(bool Code, bool Tests, bool Ci, bool PullRequest, bool Build, bool Artifact, bool RequestedOutput)
{
    public bool AllSatisfied => Code && Tests && Ci && PullRequest && Build && Artifact && RequestedOutput;
}

public sealed record AutonomousTaskCheckpoint(string TaskId, AutonomousTaskPhase Phase, string ConversationKey,
    int ConversationGeneration, AutonomousAcceptanceGates Gates, DateTimeOffset UpdatedUtc, string Reason);

public sealed class AutonomousTaskController
{
    private readonly string _checkpointPath;
    private readonly object _sync = new();
    private AutonomousTaskCheckpoint _current;

    public AutonomousTaskController(string checkpointPath, string taskId, string conversationKey)
    {
        _checkpointPath = checkpointPath;
        _current = Load(checkpointPath) ?? new(taskId, AutonomousTaskPhase.TaskPending, conversationKey, 1,
            new(false, false, false, false, false, false, false), DateTimeOffset.UtcNow, "created");
    }

    public AutonomousTaskCheckpoint Snapshot { get { lock (_sync) return _current; } }

    public void Transition(AutonomousTaskPhase phase, string reason)
    {
        lock (_sync) PersistLocked(_current with { Phase = phase, UpdatedUtc = DateTimeOffset.UtcNow, Reason = reason });
    }

    public void Rollover(string newConversationKey)
    {
        lock (_sync) PersistLocked(_current with { Phase = AutonomousTaskPhase.ConversationRollover,
            ConversationKey = newConversationKey, ConversationGeneration = _current.ConversationGeneration + 1,
            UpdatedUtc = DateTimeOffset.UtcNow, Reason = "verified conversation rollover" });
    }

    public bool TryComplete(bool assistantClaimedDone, AutonomousAcceptanceGates gates)
    {
        lock (_sync)
        {
            var completed = gates.AllSatisfied;
            PersistLocked(_current with { Gates = gates,
                Phase = completed ? AutonomousTaskPhase.Completed : AutonomousTaskPhase.VerifyingCompletion,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Reason = completed ? "all acceptance gates verified" : assistantClaimedDone ? "assistant claim is not completion evidence" : "acceptance evidence incomplete" });
            return completed;
        }
    }

    private void PersistLocked(AutonomousTaskCheckpoint checkpoint)
    {
        var directory = Path.GetDirectoryName(_checkpointPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _checkpointPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint));
        File.Move(temporary, _checkpointPath, true);
        _current = checkpoint;
    }

    private static AutonomousTaskCheckpoint? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<AutonomousTaskCheckpoint>(File.ReadAllText(path));
    }
}
