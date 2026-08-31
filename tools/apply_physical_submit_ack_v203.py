from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:160]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_all_if_present(path: str, old: str, new: str) -> None:
    p = ROOT / path
    if not p.exists():
        return
    text = p.read_text(encoding="utf-8")
    if old in text:
        p.write_text(text.replace(old, new), encoding="utf-8")


chrome = "src/GPTDeskTop/Services/ChromeDevToolsService.cs"

# Treat ChatGPT's global Too Many Requests modal as a hard composer interlock.
replace_once(
    chrome,
    "HasRenderedError: !string.IsNullOrWhiteSpace(chatState.ErrorText));",
    "HasRenderedError: !string.IsNullOrWhiteSpace(chatState.ErrorText)\n                || !string.IsNullOrWhiteSpace(chatState.GlobalRateLimitText));",
)

# Add an immediate post-click observer. A JavaScript click returning true is NOT proof that
# ChatGPT accepted the user turn. We require server/UI transition evidence, or classify a
# still-ready unchanged composer as a non-accepted click that may be retried a bounded number
# of times without consuming the exactly-once submit budget.
marker = """    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)\n"""
helper = r'''    private enum ImmediatePhysicalSubmitObservation
    {
        ReceiptConfirmed,
        AcceptedTransition,
        ClickNotAccepted,
        Ambiguous
    }

    private async Task<(bool Present, string Text)> ReadComposerTextAsync(
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        const string expression = """
        (() => {
          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
          if (!editor) return { present: false, text: '' };
          const text = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
            ? editor.value
            : (editor.innerText || editor.textContent || '');
          return { present: true, text: (text || '').trim() };
        })()
        """;
        var value = await EvaluateAsync(tab, expression, cancellationToken, false);
        return (
            value.TryGetProperty("present", out var present) && present.GetBoolean(),
            value.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty);
    }

    private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync(
        ChromeTab tab,
        string expected,
        int baselineUserTurnCount,
        CancellationToken cancellationToken)
    {
        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(2);
        var stableStillReadyReads = 0;

        while (DateTimeOffset.UtcNow < observationDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (snapshot.Success
                && snapshot.Count > baselineUserTurnCount
                && string.Equals(snapshot.LastText, expected, StringComparison.Ordinal))
                return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;

            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
            if (readiness.HasRenderedError)
                return ImmediatePhysicalSubmitObservation.Ambiguous;
            if (readiness.IsGenerating)
                return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;

            var composer = await ReadComposerTextAsync(tab, cancellationToken);
            if (!composer.Present)
                return ImmediatePhysicalSubmitObservation.Ambiguous;

            if (composer.Text.Length == 0)
                return ImmediatePhysicalSubmitObservation.AcceptedTransition;

            if (!string.Equals(composer.Text, expected, StringComparison.Ordinal))
                return ImmediatePhysicalSubmitObservation.Ambiguous;

            if (readiness.SendButtonPresent && readiness.SendButtonEnabled)
            {
                stableStillReadyReads++;
                if (stableStillReadyReads >= 3)
                    return ImmediatePhysicalSubmitObservation.ClickNotAccepted;
            }
            else
            {
                stableStillReadyReads = 0;
            }

            await Task.Delay(200, cancellationToken);
        }

        return stableStillReadyReads > 0
            ? ImmediatePhysicalSubmitObservation.ClickNotAccepted
            : ImmediatePhysicalSubmitObservation.Ambiguous;
    }

'''
p = ROOT / chrome
text = p.read_text(encoding="utf-8")
if "private enum ImmediatePhysicalSubmitObservation" not in text:
    if text.count(marker) != 1:
        raise RuntimeError("Could not locate RefreshStuckComposerAsync insertion marker")
    p.write_text(text.replace(marker, helper + marker, 1), encoding="utf-8")

# Track bounded no-acceptance clicks separately from unknown/accepted physical submits.
replace_once(
    chrome,
    """        const int maxSubmitAttempts = 2;\n        var receiptGrace = TimeSpan.FromSeconds(3);\n""",
    """        const int maxSubmitAttempts = 2;\n        const int maxUnacceptedClickAttempts = 3;\n        var receiptGrace = TimeSpan.FromSeconds(3);\n""",
)
replace_once(
    chrome,
    """        var stuckRefreshUsed = false;\n        var submitAttempts = 0;\n        var preSubmitConflictStableReads = 0;\n""",
    """        var stuckRefreshUsed = false;\n        var submitAttempts = 0;\n        var unacceptedClickAttempts = 0;\n        var preSubmitConflictStableReads = 0;\n""",
)
replace_once(
    chrome,
    """                    unacknowledgedSubmitSinceUtc = null;\n                    sendBlockedSinceUtc = null;\n                    VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"stable-absence-after-rebind\", submitAttempts);\n""",
    """                    unacknowledgedSubmitSinceUtc = null;\n                    sendBlockedSinceUtc = null;\n                    unacceptedClickAttempts = 0;\n                    VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"stable-absence-after-rebind\", submitAttempts);\n""",
)

old_tail = """            submitAttempts++;\n            unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;\n            VerifiedSendDiagnostics.Record(\"AwaitingReceipt\", \"physical-submit-unacknowledged\", submitAttempts);\n\n            await Task.Delay(300, cancellationToken);\n            var after = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n            if (after.Success && after.Count > before.Count && string.Equals(after.LastText, expected, StringComparison.Ordinal))\n            {\n                VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\", \"immediate-user-turn-observed\", submitAttempts);\n                return true;\n            }\n"""
new_tail = """            ImmediatePhysicalSubmitObservation immediateObservation;\n            try\n            {\n                immediateObservation = await ObserveImmediatePhysicalSubmitAsync(\n                    tab,\n                    expected,\n                    before.Count,\n                    cancellationToken);\n            }\n            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))\n            {\n                // The click may have reached ChatGPT but the observation channel failed. This is an\n                // unknown physical outcome, so consume exactly-once authority and reconcile read-only.\n                submitAttempts++;\n                unacceptedClickAttempts = 0;\n                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;\n                _sessionPool.Invalidate(tab.Id);\n                VerifiedSendDiagnostics.Record(\"AwaitingReceipt\", \"post-click-observation-unreadable\", submitAttempts);\n                await Task.Delay(250, cancellationToken);\n                continue;\n            }\n\n            if (immediateObservation == ImmediatePhysicalSubmitObservation.ReceiptConfirmed)\n            {\n                submitAttempts++;\n                VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\", \"physical-submit-confirmed-immediately\", submitAttempts);\n                return true;\n            }\n\n            if (immediateObservation == ImmediatePhysicalSubmitObservation.AcceptedTransition)\n            {\n                submitAttempts++;\n                unacceptedClickAttempts = 0;\n                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;\n                VerifiedSendDiagnostics.Record(\"AwaitingReceipt\", \"physical-submit-transition-observed\", submitAttempts);\n                continue;\n            }\n\n            if (immediateObservation == ImmediatePhysicalSubmitObservation.ClickNotAccepted)\n            {\n                unacceptedClickAttempts++;\n                VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"click-not-accepted-composer-still-ready\", submitAttempts);\n                if (unacceptedClickAttempts >= maxUnacceptedClickAttempts)\n                {\n                    VerifiedSendDiagnostics.Record(\"FailedClosed\", \"physical-click-not-accepted\", submitAttempts);\n                    return false;\n                }\n\n                // No user turn, no generation, and the exact expected text is still sitting in an\n                // enabled composer. The previous DOM click did not become a physical submit, so one\n                // bounded click retry is safe and does not consume the exactly-once submit budget.\n                await Task.Delay(350, cancellationToken);\n                continue;\n            }\n\n            // The click happened but the UI no longer gives enough evidence to prove either rejection\n            // or acceptance. Fail into read-only reconciliation and never blind-click again.\n            submitAttempts++;\n            unacceptedClickAttempts = 0;\n            unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;\n            VerifiedSendDiagnostics.Record(\"AwaitingReceipt\", \"physical-submit-ambiguous-after-click\", submitAttempts);\n            continue;\n"""
replace_once(chrome, old_tail, new_tail)

# v2.0.3 identifies the field closure for false-positive physical clicks.
for project in [
    "src/GPTDeskTop/GPTDeskTop.csproj",
    "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
    "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
    "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs",
]:
    replace_all_if_present(project, "2.0.2", "2.0.3")
replace_all_if_present("src/GPTDeskTop.Setup/Program.cs", 'internal const string Version = "2.0.0";', 'internal const string Version = "2.0.3";')
replace_all_if_present("src/GPTDeskTop.Setup/Program.cs", 'internal const string Version = "2.0.2";', 'internal const string Version = "2.0.3";')

# Regression contract for the exact field failure observed in v2.0.2.
regression = ROOT / "tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs"
regression.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class PhysicalSubmitAcceptanceRegressionTests
{
    [Fact]
    public void JavascriptClickAloneIsNotTreatedAsPhysicalSubmitAcceptance()
    {
        var source = ChromeSource();
        var send = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");

        Assert.Contains("maxUnacceptedClickAttempts = 3", send, StringComparison.Ordinal);
        Assert.Contains("ObserveImmediatePhysicalSubmitAsync", send, StringComparison.Ordinal);
        Assert.Contains("ImmediatePhysicalSubmitObservation.ClickNotAccepted", send, StringComparison.Ordinal);
        Assert.Contains("click-not-accepted-composer-still-ready", send, StringComparison.Ordinal);
        Assert.Contains("physical-click-not-accepted", send, StringComparison.Ordinal);
        Assert.Contains("physical-submit-ambiguous-after-click", send, StringComparison.Ordinal);
        Assert.DoesNotContain("\"physical-submit-unacknowledged\"", send, StringComparison.Ordinal);
    }

    [Fact]
    public void StillReadyUnchangedComposerMayRetryOnlyWithoutConsumingExactlyOnceBudget()
    {
        var source = ChromeSource();
        var send = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");
        var branchStart = send.IndexOf("if (immediateObservation == ImmediatePhysicalSubmitObservation.ClickNotAccepted)", StringComparison.Ordinal);
        var branchEnd = send.IndexOf("// The click happened but the UI no longer gives enough evidence", branchStart, StringComparison.Ordinal);
        Assert.True(branchStart >= 0 && branchEnd > branchStart);
        var branch = send[branchStart..branchEnd];

        Assert.Contains("unacceptedClickAttempts++", branch, StringComparison.Ordinal);
        Assert.Contains("unacceptedClickAttempts >= maxUnacceptedClickAttempts", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("submitAttempts++", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("unacknowledgedSubmitSinceUtc =", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmediateObserverRequiresRealTransitionEvidenceAndNeverReloads()
    {
        var source = ChromeSource();
        var observer = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> RefreshStuckComposerAsync");

        Assert.Contains("snapshot.Count > baselineUserTurnCount", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.IsGenerating", observer, StringComparison.Ordinal);
        Assert.Contains("composer.Text.Length == 0", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent && readiness.SendButtonEnabled", observer, StringComparison.Ordinal);
        Assert.Contains("stableStillReadyReads >= 3", observer, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", observer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", observer, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalRateLimitModalBlocksComposerSubmission()
    {
        var source = ChromeSource();
        var readiness = Slice(source, "private async Task<ComposerReadinessSnapshot> ReadComposerReadinessAsync", "private async Task<ComposerAutomationDecision> ReadComposerDecisionAsync");
        Assert.Contains("chatState.GlobalRateLimitText", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentityIsV203IncludingInstallerRegistryVersion()
    {
        var root = Root();
        Assert.Contains("<Version>2.0.3</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "GPTDeskTop.csproj")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.3</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj")), StringComparison.Ordinal);
        Assert.Contains("internal const string Version = \"2.0.3\";", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "Program.cs")), StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
''', encoding="utf-8")

print("v2.0.3 physical submit acknowledgement hotfix applied")
