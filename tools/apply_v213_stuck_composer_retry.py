from pathlib import Path

root = Path(__file__).resolve().parents[1]
chrome_path = root / 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
chrome = chrome_path.read_text(encoding='utf-8')

old = '''            stableEmptyComposerReads = 0;\n            if (!ComposerEvidenceTextEquals(composer.Text, expected))\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n\n            if (readiness.SendButtonPresent && readiness.SendButtonEnabled)\n            {\n                stableStillReadyReads++;\n                if (stableStillReadyReads >= 3)\n                    return ImmediatePhysicalSubmitObservation.ClickNotAccepted;\n            }\n            else\n            {\n                stableStillReadyReads = 0;\n            }\n'''
new = '''            stableEmptyComposerReads = 0;\n            if (!ComposerEvidenceTextEquals(composer.Text, expected))\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n\n            // Field evidence can leave the exact expected prompt visibly parked in an enabled\n            // composer while ChatGPT transiently changes/rehydrates the send button markup.\n            // Exact text + unchanged user-turn count + idle editor over repeated authoritative\n            // reads proves that the physical click was not accepted. Do not require the send\n            // button selector itself to remain stable before authorizing a bounded retry.\n            if (readiness.EditorPresent && readiness.EditorEnabled)\n            {\n                stableStillReadyReads++;\n                if (stableStillReadyReads >= 6)\n                {\n                    VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"exact-composer-still-present-after-click\", 0);\n                    return ImmediatePhysicalSubmitObservation.ClickNotAccepted;\n                }\n            }\n            else\n            {\n                stableStillReadyReads = 0;\n            }\n'''
if old not in chrome:
    raise SystemExit('Immediate physical-submit observation anchor not found')
chrome = chrome.replace(old, new, 1)

old2 = '''                if (!composerReadiness.IsGenerating\n                    && !composerReadiness.HasRenderedError\n                    && composerReadiness.EditorPresent\n                    && composerReadiness.EditorEnabled\n                    && composerReadiness.SendButtonPresent\n                    && composerReadiness.SendButtonEnabled\n                    && composer.Present\n                    && ComposerEvidenceTextEquals(composer.Text, expected))\n                {\n                    stableReadyComposerReads++;\n                    if (stableReadyComposerReads >= 3)\n                    {\n                        VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"stable-composer-proves-submit-not-accepted\", 0);\n                        return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;\n                    }\n                }\n'''
new2 = '''                if (!composerReadiness.IsGenerating\n                    && !composerReadiness.HasRenderedError\n                    && composerReadiness.EditorPresent\n                    && composerReadiness.EditorEnabled\n                    && composer.Present\n                    && ComposerEvidenceTextEquals(composer.Text, expected))\n                {\n                    stableReadyComposerReads++;\n                    if (stableReadyComposerReads >= 6)\n                    {\n                        VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"stable-exact-composer-proves-submit-not-accepted\", 0);\n                        return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;\n                    }\n                }\n'''
if old2 not in chrome:
    raise SystemExit('Reconciliation stable-composer anchor not found')
chrome = chrome.replace(old2, new2, 1)
chrome_path.write_text(chrome, encoding='utf-8')

repls = {
    root/'src/GPTDeskTop/GPTDeskTop.csproj': [('2.0.12','2.0.13')],
    root/'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj': [('2.0.12','2.0.13')],
    root/'src/GPTDeskTop.Setup/Program.cs': [('Version = \"2.0.12\"','Version = \"2.0.13\"')],
    root/'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj': [('v2.0.12','v2.0.13')],
}
for path, pairs in repls.items():
    text = path.read_text(encoding='utf-8')
    for a,b in pairs:
        if a not in text:
            raise SystemExit(f'missing version anchor {a} in {path}')
        text = text.replace(a,b)
    path.write_text(text, encoding='utf-8')

# Add focused regression coverage.
test_path = root / 'tests/GPTDeskTop.RuntimeTests/StuckComposerClickRetryRegressionTests.cs'
test_path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class StuckComposerClickRetryRegressionTests
{
    [Fact]
    public void ImmediateObservationDoesNotDependOnSendButtonSelectorForSafeRetry()
    {
        var source = ChromeSource();
        var method = Slice(source,
            "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync",
            "private async Task<bool> TryDispatchNativeSendClickAsync");

        Assert.Contains("exact-composer-still-present-after-click", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorPresent && readiness.EditorEnabled", method, StringComparison.Ordinal);
        Assert.DoesNotContain("if (readiness.SendButtonPresent && readiness.SendButtonEnabled)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationAuthorizesRetryFromStableExactComposerWithoutSendButtonSelector()
    {
        var source = ChromeSource();
        var method = Slice(source,
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("stable-exact-composer-proves-submit-not-accepted", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEvidenceTextEquals(composer.Text, expected)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("&& composerReadiness.SendButtonPresent", method, StringComparison.Ordinal);
    }

    [Fact]
    public void StableExactComposerRequiresSixReadsBeforeRetryAuthorization()
    {
        var source = ChromeSource();
        Assert.Contains("stableStillReadyReads >= 6", source, StringComparison.Ordinal);
        Assert.Contains("stableReadyComposerReads >= 6", source, StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
''', encoding='utf-8')
