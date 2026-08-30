using GPTDeskTop.Runtime;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class AutonomousTaskControllerTests
{
    [Fact]
    public void CheckpointSurvivesRestartAndRolloverPreservesTaskIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gptdesktop-task-{Guid.NewGuid():N}.json");
        try
        {
            var first = new AutonomousTaskController(path, "task-1", "chat-1");
            first.Transition(AutonomousTaskPhase.WaitingForChatGpt, "generation active");
            first.Rollover("chat-2");

            var restored = new AutonomousTaskController(path, "ignored", "ignored").Snapshot;
            Assert.Equal("task-1", restored.TaskId);
            Assert.Equal("chat-2", restored.ConversationKey);
            Assert.Equal(2, restored.ConversationGeneration);
            Assert.Equal(AutonomousTaskPhase.ConversationRollover, restored.Phase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AssistantDoneClaimCannotCompleteWithoutAcceptanceEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gptdesktop-task-{Guid.NewGuid():N}.json");
        try
        {
            var controller = new AutonomousTaskController(path, "task-2", "chat-1");
            Assert.False(controller.TryComplete(true, new(true, true, false, false, true, false, true)));
            Assert.Equal(AutonomousTaskPhase.VerifyingCompletion, controller.Snapshot.Phase);
            Assert.True(controller.TryComplete(false, new(true, true, true, true, true, true, true)));
            Assert.Equal(AutonomousTaskPhase.Completed, controller.Snapshot.Phase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MonitorIntegrationRestoresRealSavedMonitorLifecycleCheckpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gptdesktop-integration-{Guid.NewGuid():N}");
        try
        {
            var first = new MonitorAutonomousTaskIntegration(directory);
            first.StartOrRestore(77, "chat-1");
            first.Transition(77, "chat-1", AutonomousTaskPhase.WaitingForChatGpt, "generating");

            var restarted = new MonitorAutonomousTaskIntegration(directory);
            var restored = restarted.StartOrRestore(77, "chat-1");
            Assert.Equal("monitor:77", restored.TaskId);
            Assert.Equal(AutonomousTaskPhase.WaitingForChatGpt, restored.Phase);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
