using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class HandoffContinuityCheckpointRegressionTests
{
    [Fact]
    public void DeliveryTimeoutFreshChatCarriesConfirmedWorkCheckpointAndNewTargetScope()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = source.IndexOf("if (isError && IsDeliveryTimeout(text))", StringComparison.Ordinal);
        var end = source.IndexOf("if (isError)", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = source[start..end];

        Assert.Contains("new ConversationHandoffService(_database)", block, StringComparison.Ordinal);
        Assert.Contains("HandoffCheckpointPrepared", block, StringComparison.Ordinal);
        Assert.Contains("ConversationHandoffCheckpointStore.MarkTargetCreatedAsync", block, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url)", block, StringComparison.Ordinal);
        Assert.Contains("ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync", block, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedDeliveryTimeoutCommitFailureKeepsSourceHandledUntilCheckpointReconciles()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var recoveryStart = source.IndexOf("if (isError && IsDeliveryTimeout(text))", StringComparison.Ordinal);
        var commitFailure = source.IndexOf("if (committedRecoveryTab is null)", recoveryStart, StringComparison.Ordinal);
        var commitFailureEnd = source.IndexOf("continue;", commitFailure, StringComparison.Ordinal);
        Assert.True(commitFailure > recoveryStart && commitFailureEnd > commitFailure);
        var block = source[commitFailure..commitFailureEnd];

        Assert.Contains("lastHandledText = text", block, StringComparison.Ordinal);
        Assert.DoesNotContain("lastHandledText = string.Empty", block, StringComparison.Ordinal);
        Assert.Contains("checkpointed", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandoffPacketContainsExplicitConfirmedCheckpoint()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ConversationHandoffService.cs");
        Assert.Contains("نقطة الاستكمال المؤكدة", source, StringComparison.Ordinal);
        Assert.Contains("آخر طلب/تعليمات Outbound مؤكدة", source, StringComparison.Ordinal);
        Assert.Contains("Source conversation", source, StringComparison.Ordinal);
        Assert.Contains("HandoffCheckpoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedPendingHandoffIsRecoveredBeforeStartupFollowUp()
    {
        var source = Source("src", "GPTDeskTop", "Services", "LastWorkingStateService.cs");
        var pending = source.IndexOf("pendingHandoffCompleted", StringComparison.Ordinal);
        var followUp = source.IndexOf("SendExistingTabStartupFollowUpAsync", pending, StringComparison.Ordinal);
        Assert.True(pending >= 0 && followUp > pending);
        Assert.Contains("!pendingHandoffCompleted", source[pending..followUp], StringComparison.Ordinal);
        Assert.Contains("PendingHandoffRecoveredWithoutDuplicateFollowUp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialCdpRecoveryRemainsActiveInsteadOfStoppingAfterThreeAttempts()
    {
        var source = Source("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = source.IndexOf("private async Task<ChatPageState> GetChatStateWithRetryAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool IsTransientChromeException", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("for (var attempt = 1; ; attempt++)", method, StringComparison.Ordinal);
        Assert.Contains("Monitor remains active and will keep self-healing", method, StringComparison.Ordinal);
        Assert.DoesNotContain("for (var attempt = 1; attempt <= 3", method, StringComparison.Ordinal);
        Assert.DoesNotContain("throw last", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandoffCheckpointPersistsAcrossStoreInstancesUntilExplicitlyCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var database = new LocalDatabase(Path.Combine(root, "handoff.db"));
            await database.InitializeAsync();

            var monitor = new SavedMonitor
            {
                Id = 41,
                Url = "https://chatgpt.com/c/source-checkpoint",
                Title = "Source"
            };
            var sourceTab = new ChromeTab
            {
                Id = "source-tab",
                Title = "Source",
                Url = monitor.Url
            };
            var targetTab = new ChromeTab
            {
                Id = "target-tab",
                Title = "Target",
                Url = "https://chatgpt.com/c/target-checkpoint"
            };

            await ConversationHandoffCheckpointStore.PrepareAsync(
                database,
                monitor,
                sourceTab,
                "DeliveryTimeout",
                "continue from checkpoint",
                "message delivery timed out",
                "RecoveredToNewChat",
                "RecoverySent",
                "RecoveryCommitDeferred",
                incrementRotationCount: false,
                recordRotation: false);

            var prepared = await ConversationHandoffCheckpointStore.LoadAsync(database, monitor.Id);
            Assert.NotNull(prepared);
            Assert.Equal("Prepared", prepared!.Stage);
            Assert.Equal(monitor.Url, prepared.SourceUrl);
            Assert.Equal("continue from checkpoint", prepared.StartMessage);

            await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(database, monitor.Id, targetTab);
            var targetCreated = await ConversationHandoffCheckpointStore.LoadAsync(database, monitor.Id);
            Assert.NotNull(targetCreated);
            Assert.Equal("TargetCreated", targetCreated!.Stage);
            Assert.Equal(targetTab.Id, targetCreated.TargetTabId);
            Assert.Equal(targetTab.Url, targetCreated.TargetUrl);

            await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(database, monitor.Id, targetTab);
            var accepted = await ConversationHandoffCheckpointStore.LoadAsync(database, monitor.Id);
            Assert.NotNull(accepted);
            Assert.Equal("DeliveryAccepted", accepted!.Stage);
            Assert.Equal(targetTab.Url, accepted.TargetUrl);

            var reopenedDatabase = new LocalDatabase(Path.Combine(root, "handoff.db"));
            var afterRestart = await ConversationHandoffCheckpointStore.LoadAsync(reopenedDatabase, monitor.Id);
            Assert.NotNull(afterRestart);
            Assert.Equal("DeliveryAccepted", afterRestart!.Stage);
            Assert.Equal("continue from checkpoint", afterRestart.StartMessage);

            await ConversationHandoffCheckpointStore.ClearAsync(reopenedDatabase, monitor.Id);
            Assert.Null(await ConversationHandoffCheckpointStore.LoadAsync(database, monitor.Id));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static string Source(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
