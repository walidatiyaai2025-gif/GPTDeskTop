using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendTaskCancellationSelfHealRegressionTests
{
    [Fact]
    public void NonOperatorTaskCanceledIsRecoverableCdpTransport()
    {
        var method = typeof(ChromeDevToolsService).GetMethod(
            "IsRecoverableMonitorTransportException",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ChromeDevToolsService).FullName, "IsRecoverableMonitorTransportException");

        var recoverable = Assert.IsType<bool>(method.Invoke(null, new object[] { new TaskCanceledException("field target command timed out") }));
        Assert.True(recoverable);
    }

    [Fact]
    public void RequestedCancellationStillHasPriorityOverTransportRecovery()
    {
        var source = ChromeSource();
        var refresh = Slice(source, "private async Task<bool> RefreshStuckComposerAsync", "public async Task<bool> SendChatMessageAsync");
        var readable = Slice(source, "private async Task<bool> WaitForReadableConversationStateAsync", "private async Task<List<ChromeTab>?> TryGetLiveTabsAsync");

        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", refresh, StringComparison.Ordinal);
        Assert.Contains("!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex)", refresh, StringComparison.Ordinal);
        Assert.Contains("!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex)", readable, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationAfterPhysicalSubmitIsAcceptanceEvidenceNotADeadlineWait()
    {
        var method = Slice(ChromeSource(), "public async Task<bool> SendChatMessageVerifiedAsync", "private enum UnacknowledgedSubmitReconciliationResult");
        var generation = method.IndexOf("if (pendingReadiness.IsGenerating)", StringComparison.Ordinal);
        var confirmed = method.IndexOf("VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\", \"generation-after-submit\"", generation, StringComparison.Ordinal);
        var returned = method.IndexOf("return true;", confirmed, StringComparison.Ordinal);

        Assert.True(generation >= 0 && confirmed > generation && returned > confirmed);
        Assert.DoesNotContain("AwaitingReceipt\", \"generation-after-submit", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostSubmitTransportInterruptionKeepsSameLogicalOperationAlive()
    {
        var method = Slice(ChromeSource(), "public async Task<bool> SendChatMessageVerifiedAsync", "private enum UnacknowledgedSubmitReconciliationResult");

        Assert.Contains("while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)", method, StringComparison.Ordinal);
        Assert.Contains("current.Count != before.Count && unacknowledgedSubmitSinceUtc is null", method, StringComparison.Ordinal);
        Assert.Contains("UnacknowledgedSubmitReconciliationResult.TransientInterruption", method, StringComparison.Ordinal);
        Assert.Contains("\"Reconciling\", \"transient-transport-recovery\"", method, StringComparison.Ordinal);
        Assert.Contains("await TryRefreshTabBindingAsync(tab, cancellationToken)", method, StringComparison.Ordinal);

        var transient = Slice(method, "if (reconciliation == UnacknowledgedSubmitReconciliationResult.TransientInterruption)", "VerifiedSendDiagnostics.Record(\"FailedClosed\", \"ambiguous-post-submit-reconciliation\"");
        Assert.DoesNotContain("unacknowledgedSubmitSinceUtc = null", transient, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageAsync", transient, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationSeparatesTransportHydrationFromGenuineConflict()
    {
        var method = Slice(
            ChromeSource(),
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("return UnacknowledgedSubmitReconciliationResult.TransientInterruption", method, StringComparison.Ordinal);
        Assert.Contains("PostRefreshUserTurnObservation.Hydrating", method, StringComparison.Ordinal);
        Assert.Contains("stableUnexpectedReads >= 2", method, StringComparison.Ordinal);
        Assert.DoesNotContain("if (receiptBeforeRefresh.Count != baselineUserTurnCount)", method, StringComparison.Ordinal);
        Assert.Contains("return UnacknowledgedSubmitReconciliationResult.Ambiguous", method, StringComparison.Ordinal);
        Assert.Contains("if (readiness.IsGenerating)\n                    return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyOncePhysicalRetryBoundaryRemainsBoundedAndEvidenceDriven()
    {
        var method = Slice(ChromeSource(), "public async Task<bool> SendChatMessageVerifiedAsync", "private enum UnacknowledgedSubmitReconciliationResult");

        Assert.Contains("const int maxSubmitAttempts = 2", method, StringComparison.Ordinal);
        Assert.Contains("RetryAuthorized", method, StringComparison.Ordinal);
        Assert.Contains("stable-absence-after-refresh", method, StringComparison.Ordinal);
        Assert.Equal(1, Count(method, "submitted = await SendChatMessageAsync(tab, message, cancellationToken)"));
        Assert.DoesNotContain("Task.Run", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", method, StringComparison.Ordinal);
    }

    private static string ChromeSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }
}
