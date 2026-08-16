namespace GPTDeskTop.RuntimeTests;

public sealed class HandoffContinuityCheckpointRegressionTests
{
    private static string Source(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            parts));
        return File.ReadAllText(path);
    }

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
        Assert.DoesNotContain("attempt <= 3", method, StringComparison.Ordinal);
    }
}
