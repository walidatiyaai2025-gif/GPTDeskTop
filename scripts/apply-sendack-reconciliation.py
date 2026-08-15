from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

service_path = ROOT / "src" / "GPTDeskTop" / "Services" / "ChromeDevToolsService.cs"
service = service_path.read_text(encoding="utf-8")
start_marker = "    public async Task<bool> SendChatMessageVerifiedAsync"
end_marker = "    private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync"
start = service.index(start_marker)
end = service.index(end_marker, start)

replacement = r'''    public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false)
    {
        var expected = message.Trim();
        if (expected.Length == 0)
        {
            VerifiedSendDiagnostics.Record("Rejected", "empty-message", 0);
            return false;
        }

        const int maxSubmitAttempts = 2;
        var receiptGrace = TimeSpan.FromSeconds(3);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        VerifiedSendDiagnostics.Record("Baseline", "reading-baseline", 0);

        var before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        while (!before.Success && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken);
            before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        }

        if (!before.Success)
        {
            VerifiedSendDiagnostics.Record("FailedClosed", "baseline-unreadable", 0);
            return false;
        }

        if (string.Equals(before.LastText, expected, StringComparison.Ordinal))
        {
            var deliveryState = await GetChatStateAsync(tab, cancellationToken);
            if (MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
                    requireNewTurn,
                    before.Count,
                    deliveryState.AssistantCount,
                    deliveryState.IsGenerating))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "matching-tail-reused", 0);
                return true;
            }
        }

        DateTimeOffset? sendBlockedSinceUtc = null;
        DateTimeOffset? unacknowledgedSubmitSinceUtc = null;
        var stuckRefreshUsed = false;
        var submitAttempts = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (!current.Success)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "new-user-turn-observed", submitAttempts);
                return true;
            }

            if (current.Count != before.Count)
            {
                VerifiedSendDiagnostics.Record("FailedClosed", "unexpected-user-turn-change", submitAttempts);
                return false;
            }

            if (unacknowledgedSubmitSinceUtc is not null)
            {
                if (DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value < receiptGrace)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                try
                {
                    var pendingReadiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                    if (pendingReadiness.HasRenderedError)
                    {
                        VerifiedSendDiagnostics.Record("FailedClosed", "rendered-error-after-submit", submitAttempts);
                        return false;
                    }

                    if (pendingReadiness.IsGenerating)
                    {
                        VerifiedSendDiagnostics.Record("AwaitingReceipt", "generation-after-submit", submitAttempts);
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
                {
                    _sessionPool.Invalidate(tab.Id);
                    VerifiedSendDiagnostics.Record("AwaitingReceipt", "post-submit-state-unreadable", submitAttempts);
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                VerifiedSendDiagnostics.Record("Reconciling", "receipt-not-observed-after-grace", submitAttempts);
                var reconciliation = await ReconcileUnacknowledgedSubmitAsync(
                    tab,
                    expected,
                    before.Count,
                    cancellationToken);

                if (reconciliation == UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed)
                {
                    VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-confirmed-after-refresh", submitAttempts);
                    return true;
                }

                if (reconciliation == UnacknowledgedSubmitReconciliationResult.RetryAuthorized)
                {
                    if (submitAttempts >= maxSubmitAttempts)
                    {
                        VerifiedSendDiagnostics.Record("FailedClosed", "retry-limit-reached-without-receipt", submitAttempts);
                        return false;
                    }

                    unacknowledgedSubmitSinceUtc = null;
                    sendBlockedSinceUtc = null;
                    VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-absence-after-refresh", submitAttempts);
                    continue;
                }

                VerifiedSendDiagnostics.Record("FailedClosed", "ambiguous-post-submit-reconciliation", submitAttempts);
                return false;
            }

            ComposerAutomationDecision preparationDecision;
            try
            {
                preparationDecision = await ReadComposerDecisionAsync(tab, requireSendReady: false, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                _sessionPool.Invalidate(tab.Id);
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (preparationDecision != ComposerAutomationDecision.ReadyToPrepare)
            {
                sendBlockedSinceUtc = null;
                await Task.Delay(500, cancellationToken);
                continue;
            }

            bool submitted;
            try
            {
                submitted = await SendChatMessageAsync(tab, message, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                // SendChatMessageAsync mutates the editor before the final Runtime.evaluate click.
                // A transport loss here has an unknown physical outcome, so reconcile before any retry.
                submitAttempts++;
                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
                _sessionPool.Invalidate(tab.Id);
                VerifiedSendDiagnostics.Record("AwaitingReceipt", "transport-uncertain-submit", submitAttempts);
                await Task.Delay(250, cancellationToken);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "ChromeDevToolsService.SendChatMessageVerified", null, tab.Id, tab.Title);
                VerifiedSendDiagnostics.Record("FailedClosed", "nonrecoverable-send-exception", submitAttempts);
                return false;
            }

            if (!submitted)
            {
                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                if (readiness.IsPostGenerationSendBlocked)
                {
                    var now = DateTimeOffset.UtcNow;
                    sendBlockedSinceUtc ??= now;
                    var editorMatchesExpected = await ComposerEditorMatchesExpectedAsync(tab, expected, cancellationToken);
                    var blockedFor = now - sendBlockedSinceUtc.Value;

                    if (StuckComposerRecoveryPolicy.ShouldRefresh(
                            readiness,
                            editorMatchesExpected,
                            blockedFor,
                            stuckRefreshUsed))
                    {
                        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
                        if (receiptBeforeRefresh.Success
                            && receiptBeforeRefresh.Count > before.Count
                            && string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal))
                        {
                            VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-before-stuck-refresh", submitAttempts);
                            return true;
                        }

                        stuckRefreshUsed = true;
                        sendBlockedSinceUtc = null;
                        if (await RefreshStuckComposerAsync(tab, cancellationToken))
                        {
                            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
                            if (receiptAfterRefresh.Success
                                && receiptAfterRefresh.Count > before.Count
                                && string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal))
                            {
                                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-after-stuck-refresh", submitAttempts);
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    sendBlockedSinceUtc = null;
                }

                await Task.Delay(500, cancellationToken);
                continue;
            }

            submitAttempts++;
            unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
            VerifiedSendDiagnostics.Record("AwaitingReceipt", "physical-submit-unacknowledged", submitAttempts);

            await Task.Delay(300, cancellationToken);
            var after = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (after.Success && after.Count > before.Count && string.Equals(after.LastText, expected, StringComparison.Ordinal))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "immediate-user-turn-observed", submitAttempts);
                return true;
            }
        }

        VerifiedSendDiagnostics.Record("FailedClosed", "verified-send-deadline-without-receipt", submitAttempts);
        return false;
    }

    private enum UnacknowledgedSubmitReconciliationResult
    {
        ReceiptConfirmed,
        RetryAuthorized,
        Ambiguous
    }

    private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync(
        ChromeTab tab,
        string expected,
        int baselineUserTurnCount,
        CancellationToken cancellationToken)
    {
        var originalUrl = tab.Url;
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(originalUrl))
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;

        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        if (!receiptBeforeRefresh.Success)
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;
        if (receiptBeforeRefresh.Count > baselineUserTurnCount
            && string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal))
            return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
        if (receiptBeforeRefresh.Count != baselineUserTurnCount)
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;

        if (!await RefreshStuckComposerAsync(tab, cancellationToken))
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;
        if (!ChatGptConversationIdentity.IsSame(originalUrl, tab.Url))
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;

        var stableAbsenceReads = 0;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (!receiptAfterRefresh.Success)
            {
                stableAbsenceReads = 0;
                await Task.Delay(400, cancellationToken);
                continue;
            }

            if (receiptAfterRefresh.Count > baselineUserTurnCount
                && string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal))
                return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
            if (receiptAfterRefresh.Count != baselineUserTurnCount)
                return UnacknowledgedSubmitReconciliationResult.Ambiguous;

            try
            {
                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                if (readiness.IsGenerating || readiness.HasRenderedError)
                    return UnacknowledgedSubmitReconciliationResult.Ambiguous;
                if (!readiness.EditorPresent || !readiness.EditorEnabled)
                {
                    stableAbsenceReads = 0;
                    await Task.Delay(400, cancellationToken);
                    continue;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                stableAbsenceReads = 0;
                _sessionPool.Invalidate(tab.Id);
                await Task.Delay(400, cancellationToken);
                continue;
            }

            stableAbsenceReads++;
            if (stableAbsenceReads >= 2)
                return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;

            await Task.Delay(400, cancellationToken);
        }

        return UnacknowledgedSubmitReconciliationResult.Ambiguous;
    }
'''

service = service[:start] + replacement + service[end:]
service_path.write_text(service, encoding="utf-8")

inspector_path = ROOT / "src" / "GPTDeskTop" / "Services" / "RuntimeInspectorService.cs"
inspector = inspector_path.read_text(encoding="utf-8")

needle = '''internal sealed record RuntimeInspectorComposerDiagnostics(
    string Decision,
    string Reason,
    DateTimeOffset ObservedAtUtc);
'''
insert = needle + '''\ninternal sealed record RuntimeInspectorVerifiedSendDiagnostics(\n    string Phase,\n    string Reason,\n    int SubmitAttempts,\n    DateTimeOffset ObservedAtUtc);\n'''
if needle not in inspector:
    raise SystemExit("composer diagnostics record marker not found")
inspector = inspector.replace(needle, insert, 1)

needle = '''    RuntimeInspectorComposerDiagnostics ComposerDiagnostics,
    RuntimeInspectorUiDiagnostics UiDiagnostics,
'''
replace = '''    RuntimeInspectorComposerDiagnostics ComposerDiagnostics,
    RuntimeInspectorVerifiedSendDiagnostics VerifiedSendDiagnostics,
    RuntimeInspectorUiDiagnostics UiDiagnostics,
'''
if needle not in inspector:
    raise SystemExit("snapshot field marker not found")
inspector = inspector.replace(needle, replace, 1)

needle = '''        var composerDiagnostics = new RuntimeInspectorComposerDiagnostics(
            composerSnapshot.Decision.ToString(),
            composerSnapshot.Reason,
            composerSnapshot.ObservedAtUtc);

        var ui = new List<object>();
'''
replace = '''        var composerDiagnostics = new RuntimeInspectorComposerDiagnostics(
            composerSnapshot.Decision.ToString(),
            composerSnapshot.Reason,
            composerSnapshot.ObservedAtUtc);
        var verifiedSendSnapshot = VerifiedSendDiagnostics.Last;
        var verifiedSendDiagnostics = new RuntimeInspectorVerifiedSendDiagnostics(
            verifiedSendSnapshot.Phase,
            verifiedSendSnapshot.Reason,
            verifiedSendSnapshot.SubmitAttempts,
            verifiedSendSnapshot.ObservedAtUtc);

        var ui = new List<object>();
'''
if needle not in inspector:
    raise SystemExit("capture diagnostics marker not found")
inspector = inspector.replace(needle, replace, 1)

needle = '''            browserDiagnostics,
            composerDiagnostics,
            uiDiagnostics,
'''
replace = '''            browserDiagnostics,
            composerDiagnostics,
            verifiedSendDiagnostics,
            uiDiagnostics,
'''
if needle not in inspector:
    raise SystemExit("snapshot constructor marker not found")
inspector = inspector.replace(needle, replace, 1)

needle = '''        var composer = snapshot.ComposerDiagnostics;
        var ui = snapshot.UiDiagnostics;
'''
replace = '''        var composer = snapshot.ComposerDiagnostics;
        var verifiedSend = snapshot.VerifiedSendDiagnostics;
        var ui = snapshot.UiDiagnostics;
'''
if needle not in inspector:
    raise SystemExit("summary local marker not found")
inspector = inspector.replace(needle, replace, 1)

needle = '''               $"Composer gate: {composer.Reason} ({composer.Decision}) @ {composer.ObservedAtUtc:O}\\r\\n" +
               $"UI forms: {ui.FormsCaptured} | visible controls: {ui.VisibleControls} | visible overflows: {ui.VisibleOverflowCount}\\r\\n" +
'''
replace = '''               $"Composer gate: {composer.Reason} ({composer.Decision}) @ {composer.ObservedAtUtc:O}\\r\\n" +
               $"Verified send: {verifiedSend.Phase} | attempts: {verifiedSend.SubmitAttempts} | {verifiedSend.Reason} @ {verifiedSend.ObservedAtUtc:O}\\r\\n" +
               $"UI forms: {ui.FormsCaptured} | visible controls: {ui.VisibleControls} | visible overflows: {ui.VisibleOverflowCount}\\r\\n" +
'''
if needle not in inspector:
    raise SystemExit("summary line marker not found")
inspector = inspector.replace(needle, replace, 1)

inspector_path.write_text(inspector, encoding="utf-8")
print("SENDACK integration applied")
