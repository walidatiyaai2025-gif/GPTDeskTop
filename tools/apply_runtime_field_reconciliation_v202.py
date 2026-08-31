from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_all_required(path: str, old: str, new: str, minimum: int = 1) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    if old not in text:
        if new in text:
            return
        raise RuntimeError(f"Expected at least {minimum} matches in {path}: {old!r}")
    count = text.count(old)
    if count < minimum:
        raise RuntimeError(f"Expected at least {minimum} matches in {path}, found {count}: {old!r}")
    p.write_text(text.replace(old, new), encoding="utf-8")


chrome = "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
monitor = "src/GPTDeskTop/Services/ChatGptMonitorService.cs"

# 1) Never take the verified-send baseline from a still-hydrating conversation.
replace_once(
    chrome,
    """        var before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n        while (!before.Success && DateTimeOffset.UtcNow < deadline)\n        {\n            await Task.Delay(250, cancellationToken);\n            before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n        }\n""",
    """        var before = await WaitForStableUserMessageBaselineAsync(tab, deadline, cancellationToken);\n""",
)

replace_once(
    chrome,
    """        DateTimeOffset? sendBlockedSinceUtc = null;\n        DateTimeOffset? unacknowledgedSubmitSinceUtc = null;\n        var stuckRefreshUsed = false;\n        var submitAttempts = 0;\n""",
    """        DateTimeOffset? sendBlockedSinceUtc = null;\n        DateTimeOffset? unacknowledgedSubmitSinceUtc = null;\n        var stuckRefreshUsed = false;\n        var submitAttempts = 0;\n        var preSubmitConflictStableReads = 0;\n        var preSubmitConflictCount = -1;\n        var preSubmitConflictLastText = string.Empty;\n""",
)

# 2) A single DOM count change before submit is not enough to call a user conflict.
# Hydration can temporarily expose fewer turns; a genuine different user turn must be stable.
replace_once(
    chrome,
    """            if (current.Count != before.Count && unacknowledgedSubmitSinceUtc is null)\n            {\n                VerifiedSendDiagnostics.Record(\"FailedClosed\", \"unexpected-user-turn-change\", submitAttempts);\n                return false;\n            }\n""",
    """            if (unacknowledgedSubmitSinceUtc is null && current.Count < before.Count)\n            {\n                preSubmitConflictStableReads = 0;\n                preSubmitConflictCount = -1;\n                preSubmitConflictLastText = string.Empty;\n                VerifiedSendDiagnostics.Record(\"Baseline\", \"pre-submit-hydration-observed\", submitAttempts);\n                await Task.Delay(400, cancellationToken);\n                continue;\n            }\n\n            if (unacknowledgedSubmitSinceUtc is null && current.Count > before.Count)\n            {\n                if (current.Count == preSubmitConflictCount\n                    && string.Equals(current.LastText, preSubmitConflictLastText, StringComparison.Ordinal))\n                {\n                    preSubmitConflictStableReads++;\n                }\n                else\n                {\n                    preSubmitConflictStableReads = 1;\n                    preSubmitConflictCount = current.Count;\n                    preSubmitConflictLastText = current.LastText;\n                }\n\n                if (preSubmitConflictStableReads < 3)\n                {\n                    VerifiedSendDiagnostics.Record(\"Baseline\", \"pre-submit-user-turn-change-awaiting-stability\", submitAttempts);\n                    await Task.Delay(400, cancellationToken);\n                    continue;\n                }\n\n                VerifiedSendDiagnostics.Record(\"FailedClosed\", \"unexpected-user-turn-change\", submitAttempts);\n                return false;\n            }\n\n            if (unacknowledgedSubmitSinceUtc is null)\n            {\n                preSubmitConflictStableReads = 0;\n                preSubmitConflictCount = -1;\n                preSubmitConflictLastText = string.Empty;\n            }\n""",
)

# 3) Allow at most the explicit pre-submit stuck-composer recovery reload.
replace_once(
    chrome,
    "public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false)",
    "public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false, bool allowRecoveryReload = false)",
)
replace_once(
    chrome,
    """                    if (StuckComposerRecoveryPolicy.ShouldRefresh(\n                            readiness,\n                            editorMatchesExpected,\n                            blockedFor,\n                            stuckRefreshUsed))\n""",
    """                    if (allowRecoveryReload && StuckComposerRecoveryPolicy.ShouldRefresh(\n                            readiness,\n                            editorMatchesExpected,\n                            blockedFor,\n                            stuckRefreshUsed))\n""",
)

# 4) Post-submit reconciliation becomes read/rebind-only. No reload loop after an uncertain submit.
replace_once(
    chrome,
    """        if (!await RefreshStuckComposerAsync(tab, cancellationToken))\n            return UnacknowledgedSubmitReconciliationResult.TransientInterruption;\n        if (!ChatGptConversationIdentity.IsSame(originalUrl, tab.Url))\n            return UnacknowledgedSubmitReconciliationResult.Ambiguous;\n\n        var stableAbsenceReads = 0;\n""",
    """        // An uncertain physical submit must never drive a reload loop. Rebind to the same\n        // stable conversation and observe it read-only. Reload remains available only to the\n        // one-shot pre-submit stuck-composer recovery path.\n        await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);\n        if (!ChatGptConversationIdentity.IsSame(originalUrl, tab.Url))\n            return UnacknowledgedSubmitReconciliationResult.Ambiguous;\n\n        RuntimeFlightRecorder.Record(\"Browser\", \"PostSubmitReloadSuppressed\", \"read-only\", \"rebind-before-reconcile\", tabId: tab.Id, conversationRef: tab.Url);\n        var stableAbsenceReads = 0;\n""",
)
replace_once(chrome, "if (stableAbsenceReads >= 2)", "if (stableAbsenceReads >= 4)")
replace_all_required(chrome, "receipt-confirmed-after-refresh", "receipt-confirmed-after-rebind")
replace_all_required(chrome, "stable-absence-after-refresh", "stable-absence-after-rebind")

# Stable baseline helper: five identical, editor-ready observations spanning >=2 seconds.
insert_marker = """    private enum UnacknowledgedSubmitReconciliationResult\n    {\n"""
helper = """    private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync(\n        ChromeTab tab,\n        DateTimeOffset deadline,\n        CancellationToken cancellationToken)\n    {\n        const int stableReadsRequired = 5;\n        var stableReads = 0;\n        var stableCount = -1;\n        var stableLastText = string.Empty;\n        var stableSinceUtc = DateTimeOffset.MinValue;\n\n        while (DateTimeOffset.UtcNow < deadline)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            var snapshot = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n            if (!snapshot.Success)\n            {\n                stableReads = 0;\n                stableSinceUtc = DateTimeOffset.MinValue;\n                await Task.Delay(500, cancellationToken);\n                continue;\n            }\n\n            ComposerReadinessSnapshot readiness;\n            try\n            {\n                readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n            }\n            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))\n            {\n                _sessionPool.Invalidate(tab.Id);\n                await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);\n                stableReads = 0;\n                stableSinceUtc = DateTimeOffset.MinValue;\n                await Task.Delay(500, cancellationToken);\n                continue;\n            }\n\n            if (readiness.IsGenerating\n                || !readiness.EditorPresent\n                || !readiness.EditorEnabled\n                || readiness.HasRenderedError)\n            {\n                stableReads = 0;\n                stableSinceUtc = DateTimeOffset.MinValue;\n                await Task.Delay(500, cancellationToken);\n                continue;\n            }\n\n            var now = DateTimeOffset.UtcNow;\n            if (snapshot.Count == stableCount\n                && string.Equals(snapshot.LastText, stableLastText, StringComparison.Ordinal))\n            {\n                stableReads++;\n            }\n            else\n            {\n                stableReads = 1;\n                stableCount = snapshot.Count;\n                stableLastText = snapshot.LastText;\n                stableSinceUtc = now;\n            }\n\n            if (stableReads >= stableReadsRequired\n                && stableSinceUtc != DateTimeOffset.MinValue\n                && now - stableSinceUtc >= TimeSpan.FromSeconds(2))\n            {\n                VerifiedSendDiagnostics.Record(\"Baseline\", \"stable-editor-and-user-turn-baseline\", 0);\n                return snapshot;\n            }\n\n            await Task.Delay(500, cancellationToken);\n        }\n\n        return (false, 0, string.Empty);\n    }\n\n"""
p = ROOT / chrome
text = p.read_text(encoding="utf-8")
if "WaitForStableUserMessageBaselineAsync(" not in text.split(insert_marker)[0]:
    if text.count(insert_marker) != 1:
        raise RuntimeError("Could not locate reconciliation enum insertion marker")
    p.write_text(text.replace(insert_marker, helper + insert_marker, 1), encoding="utf-8")

# 5) Any SendWhenReady path that is not already inside the monitor poll gate must acquire the
# process-wide operation gate, and it must hold it across the verified send/reconciliation.
replace_once(
    monitor,
    """    private async Task<bool> SendWhenReadyAsync(long monitorId, ChromeTab tab, string message, bool allowRecoveryReload, CancellationToken cancellationToken)\n    {\n        try\n        {\n""",
    """    private async Task<bool> SendWhenReadyAsync(long monitorId, ChromeTab tab, string message, bool allowRecoveryReload, CancellationToken cancellationToken)\n    {\n        IDisposable? operationLease = null;\n        if (_chatOperationGate.ActiveMonitorId != monitorId)\n        {\n            operationLease = await _chatOperationGate.AcquireAsync(\n                monitorId,\n                \"send-recovery-reconciliation\",\n                cancellationToken);\n        }\n\n        try\n        {\n            try\n            {\n""",
)
replace_once(
    monitor,
    """                () => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken),\n""",
    """                () => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken, allowRecoveryReload: allowRecoveryReload),\n""",
)
replace_once(
    monitor,
    """        catch (Exception ex) when (IsTransientChromeException(ex))\n        {\n            Activity?.Invoke(monitorId, $\"Physical composer send became uncertain ({ex.GetType().Name}). Exactly-once guard suppressed automatic resend.\");\n            return false;\n        }\n    }\n\n    private async Task ApplyModelRouteAsync""",
    """            catch (Exception ex) when (IsTransientChromeException(ex))\n            {\n                Activity?.Invoke(monitorId, $\"Physical composer send became uncertain ({ex.GetType().Name}). Exactly-once guard suppressed automatic resend.\");\n                return false;\n            }\n        }\n        finally\n        {\n            operationLease?.Dispose();\n        }\n    }\n\n    private async Task ApplyModelRouteAsync""",
)

# Regression expectations and release identity.
replace_all_required(
    "tests/GPTDeskTop.RuntimeTests/VerifiedSendTaskCancellationSelfHealRegressionTests.cs",
    "stable-absence-after-refresh",
    "stable-absence-after-rebind",
)

regression_path = ROOT / "tests/GPTDeskTop.RuntimeTests/FieldSavedMonitorReconciliationRegressionTests.cs"
regression_path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class FieldSavedMonitorReconciliationRegressionTests
{
    [Fact]
    public void VerifiedSendWaitsForStableHydratedBaselineBeforeAnyPhysicalSubmit()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome, "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync", "private enum UnacknowledgedSubmitReconciliationResult");
        var send = Slice(chrome, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");

        Assert.Contains("stableReadsRequired = 5", method, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorPresent", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorEnabled", method, StringComparison.Ordinal);
        Assert.Contains("stable-editor-and-user-turn-baseline", method, StringComparison.Ordinal);
        Assert.Contains("WaitForStableUserMessageBaselineAsync(tab, deadline, cancellationToken)", send, StringComparison.Ordinal);
        Assert.Contains("pre-submit-hydration-observed", send, StringComparison.Ordinal);
        Assert.Contains("preSubmitConflictStableReads < 3", send, StringComparison.Ordinal);
    }

    [Fact]
    public void PostSubmitReconciliationIsReadOnlyAndCannotReloadLoop()
    {
        var method = Slice(
            ChromeSource(),
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("TryRefreshTabBindingAsync(tab, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("PostSubmitReloadSuppressed", method, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads >= 4", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshStuckComposerAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SendRecoveryReconciliationUsesGlobalOperationGateEvenOutsidePollCycle()
    {
        var monitor = MonitorSource();
        var method = Slice(monitor, "private async Task<bool> SendWhenReadyAsync", "private async Task ApplyModelRouteAsync");

        Assert.Contains("_chatOperationGate.ActiveMonitorId != monitorId", method, StringComparison.Ordinal);
        Assert.Contains("send-recovery-reconciliation", method, StringComparison.Ordinal);
        Assert.Contains("operationLease?.Dispose()", method, StringComparison.Ordinal);
        Assert.Contains("allowRecoveryReload: allowRecoveryReload", method, StringComparison.Ordinal);
    }

    private static string ChromeSource() => ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
    private static string MonitorSource() => ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
''', encoding="utf-8")

# v2.0.2 is the field-liveness/reconciliation hotfix release.
for project in [
    "src/GPTDeskTop/GPTDeskTop.csproj",
    "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
    "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
    "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs",
]:
    replace_all_required(project, "2.0.1", "2.0.2")

# Keep release notes explicit about the field failure that motivated this release.
build_path = ROOT / "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj"
build_text = build_path.read_text(encoding="utf-8")
if "Hydration-stable verified-send baseline" not in build_text:
    build_text = build_text.replace(
        "- Unacknowledged send reconciliation is bounded to preserve global monitor liveness",
        "- Hydration-stable verified-send baseline prevents false manual-user conflicts&#x0D;&#x0A;- Post-submit reconciliation is read/rebind-only; reload loops are suppressed&#x0D;&#x0A;- Unacknowledged send reconciliation is bounded to preserve global monitor liveness",
        1,
    )
    build_path.write_text(build_text, encoding="utf-8")

print("runtime field reconciliation v2.0.2 hotfix applied")
