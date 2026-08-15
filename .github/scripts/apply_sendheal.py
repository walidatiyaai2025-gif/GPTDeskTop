from pathlib import Path

chrome_path = Path('src/GPTDeskTop/Services/ChromeDevToolsService.cs')
source = chrome_path.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str) -> None:
    global source
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one match, found {count}')
    source = source.replace(old, new, 1)


replace_once(
    '''           || ex is TimeoutException\n           || ex is HttpRequestException''',
    '''           || ex is TimeoutException\n           || ex is TaskCanceledException\n           || ex is HttpRequestException''',
    'recoverable TaskCanceled')

replace_once(
    '''        while (DateTimeOffset.UtcNow < deadline)\n        {\n            cancellationToken.ThrowIfCancellationRequested();''',
    '''        // Before a physical submit the normal deadline still applies. Once a submit has\n        // an unknown outcome, elapsed time alone is never permission to abandon reconciliation: keep\n        // observing/rebinding until receipt, stable absence, a genuine conflict/error, or cancellation.\n        while (DateTimeOffset.UtcNow < deadline || unacknowledgedSubmitSinceUtc is not null)\n        {\n            cancellationToken.ThrowIfCancellationRequested();''',
    'post-submit liveness loop')

replace_once(
    '''                    if (pendingReadiness.IsGenerating)\n                    {\n                        VerifiedSendDiagnostics.Record("AwaitingReceipt", "generation-after-submit", submitAttempts);\n                        await Task.Delay(500, cancellationToken);\n                        continue;\n                    }''',
    '''                    if (pendingReadiness.IsGenerating)\n                    {\n                        // The composer was verified idle immediately before our physical submit. If the\n                        // same conversation is now generating after that submit, the server accepted a\n                        // user turn even when the user-message DOM receipt is late or temporarily absent.\n                        // Treat this as read-only acceptance evidence; never click Send again.\n                        VerifiedSendDiagnostics.Record("ReceiptConfirmed", "generation-after-submit", submitAttempts);\n                        return true;\n                    }''',
    'generation acceptance evidence')

replace_once(
    '''                if (reconciliation == UnacknowledgedSubmitReconciliationResult.RetryAuthorized)\n                {\n                    if (submitAttempts >= maxSubmitAttempts)\n                    {\n                        VerifiedSendDiagnostics.Record("FailedClosed", "retry-limit-reached-without-receipt", submitAttempts);\n                        return false;\n                    }\n\n                    unacknowledgedSubmitSinceUtc = null;\n                    sendBlockedSinceUtc = null;\n                    VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-absence-after-refresh", submitAttempts);\n                    continue;\n                }\n\n                VerifiedSendDiagnostics.Record("FailedClosed", "ambiguous-post-submit-reconciliation", submitAttempts);''',
    '''                if (reconciliation == UnacknowledgedSubmitReconciliationResult.RetryAuthorized)\n                {\n                    if (submitAttempts >= maxSubmitAttempts)\n                    {\n                        VerifiedSendDiagnostics.Record("FailedClosed", "retry-limit-reached-without-receipt", submitAttempts);\n                        return false;\n                    }\n\n                    unacknowledgedSubmitSinceUtc = null;\n                    sendBlockedSinceUtc = null;\n                    VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-absence-after-refresh", submitAttempts);\n                    continue;\n                }\n\n                if (reconciliation == UnacknowledgedSubmitReconciliationResult.TransientInterruption)\n                {\n                    // Target/session replacement and machine/browser contention are liveness events,\n                    // not proof that the submit failed. Keep the original operation in-flight and\n                    // rebind/read again. Crucially, do not clear unacknowledgedSubmitSinceUtc here.\n                    VerifiedSendDiagnostics.Record("Reconciling", "transient-transport-recovery", submitAttempts);\n                    await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);\n                    await Task.Delay(1500, cancellationToken);\n                    continue;\n                }\n\n                VerifiedSendDiagnostics.Record("FailedClosed", "ambiguous-post-submit-reconciliation", submitAttempts);''',
    'transient caller branch')

replace_once(
    '''    private enum UnacknowledgedSubmitReconciliationResult\n    {\n        ReceiptConfirmed,\n        RetryAuthorized,\n        Ambiguous\n    }''',
    '''    private enum UnacknowledgedSubmitReconciliationResult\n    {\n        ReceiptConfirmed,\n        RetryAuthorized,\n        TransientInterruption,\n        Ambiguous\n    }''',
    'reconciliation enum')

replace_once(
    '''        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n        if (!receiptBeforeRefresh.Success)\n            return UnacknowledgedSubmitReconciliationResult.Ambiguous;''',
    '''        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n        if (!receiptBeforeRefresh.Success)\n            return UnacknowledgedSubmitReconciliationResult.TransientInterruption;''',
    'pre-refresh transient')

replace_once(
    '''        if (!await RefreshStuckComposerAsync(tab, cancellationToken))\n            return UnacknowledgedSubmitReconciliationResult.Ambiguous;''',
    '''        if (!await RefreshStuckComposerAsync(tab, cancellationToken))\n            return UnacknowledgedSubmitReconciliationResult.TransientInterruption;''',
    'refresh transient')

replace_once(
    '''                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n                if (readiness.IsGenerating || readiness.HasRenderedError)\n                    return UnacknowledgedSubmitReconciliationResult.Ambiguous;\n                if (!readiness.EditorPresent || !readiness.EditorEnabled)''',
    '''                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n                if (readiness.IsGenerating)\n                    return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;\n                if (readiness.HasRenderedError)\n                    return UnacknowledgedSubmitReconciliationResult.Ambiguous;\n                if (!readiness.EditorPresent || !readiness.EditorEnabled)''',
    'post-refresh generation evidence')

replace_once(
    '''        return UnacknowledgedSubmitReconciliationResult.Ambiguous;    }\n    private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync''',
    '''        // Exhausting hydration/transport observations without stable conflicting evidence\n        // is not a user-turn conflict. Keep the original submit under reconciliation.\n        return UnacknowledgedSubmitReconciliationResult.TransientInterruption;\n    }\n\n    private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync''',
    'reconciliation exhaustion')

# Tighten recovery loops that can observe TaskCanceledException during target replacement.
refresh_start = source.index('private async Task<bool> RefreshStuckComposerAsync')
refresh_end = source.index('public async Task<bool> SendChatMessageAsync', refresh_start)
refresh_block = source[refresh_start:refresh_end]
old_catch = 'catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))'
if refresh_block.count(old_catch) != 2:
    raise SystemExit(f'RefreshStuckComposerAsync: expected two recoverable catches, found {refresh_block.count(old_catch)}')
refresh_block = refresh_block.replace(
    old_catch,
    'catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))')
source = source[:refresh_start] + refresh_block + source[refresh_end:]

readable_start = source.index('private async Task<bool> WaitForReadableConversationStateAsync')
readable_end = source.index('private async Task<List<ChromeTab>?> TryGetLiveTabsAsync', readable_start)
readable_block = source[readable_start:readable_end]
if readable_block.count(old_catch) != 1:
    raise SystemExit(f'WaitForReadableConversationStateAsync: expected one recoverable catch, found {readable_block.count(old_catch)}')
readable_block = readable_block.replace(
    old_catch,
    'catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))')
source = source[:readable_start] + readable_block + source[readable_end:]

chrome_path.write_text(source, encoding='utf-8')

test_path = Path('tests/GPTDeskTop.RuntimeTests/VerifiedSendTaskCancellationSelfHealRegressionTests.cs')
if test_path.exists():
    raise SystemExit(f'{test_path} already exists')
test_path.write_text(r'''using System.Reflection;
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
''', encoding='utf-8')
