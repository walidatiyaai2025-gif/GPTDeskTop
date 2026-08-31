from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHROME = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
PHYSICAL_TESTS = ROOT / "tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs"
OPERATOR_TESTS = ROOT / "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs"


def rw(path: Path, transform):
    before = path.read_text(encoding="utf-8")
    after = transform(before)
    if after == before:
        raise RuntimeError(f"Expected source mutation did not occur: {path}")
    path.write_text(after, encoding="utf-8")


def replace_exact(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def patch_chrome(text: str) -> str:
    text = replace_exact(
        text,
        """    private enum ImmediatePhysicalSubmitObservation\n    {\n        ReceiptConfirmed,\n        AcceptedTransition,\n        ClickNotAccepted,\n        Ambiguous\n    }\n""",
        """    private enum ImmediatePhysicalSubmitObservation\n    {\n        ReceiptConfirmed,\n        ClickNotAccepted,\n        Ambiguous\n    }\n""",
        "remove empty-composer acceptance enum",
    )

    start = text.index("    private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync")
    end = text.index("    private async Task<bool> TryDispatchNativeSendClickAsync", start)
    observer = """    private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync(\n        ChromeTab tab,\n        string expected,\n        int baselineUserTurnCount,\n        CancellationToken cancellationToken)\n    {\n        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(4);\n        var stableStillReadyReads = 0;\n        var stableEmptyComposerReads = 0;\n\n        while (DateTimeOffset.UtcNow < observationDeadline)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n\n            var snapshot = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n            if (snapshot.Success && snapshot.Count > baselineUserTurnCount)\n            {\n                if (ComposerEvidenceTextEquals(snapshot.LastText, expected))\n                    return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n            }\n\n            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n            if (readiness.HasRenderedError)\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n            if (readiness.IsGenerating)\n                return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;\n\n            var composer = await ReadComposerTextAsync(tab, cancellationToken);\n            if (!composer.Present)\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n\n            if (composer.Text.Length == 0)\n            {\n                stableStillReadyReads = 0;\n                if (readiness.EditorPresent && readiness.EditorEnabled)\n                {\n                    stableEmptyComposerReads++;\n                    if (stableEmptyComposerReads >= 8)\n                    {\n                        VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"composer-cleared-without-user-turn\", 0);\n                        return ImmediatePhysicalSubmitObservation.ClickNotAccepted;\n                    }\n                }\n                else\n                {\n                    stableEmptyComposerReads = 0;\n                }\n                await Task.Delay(250, cancellationToken);\n                continue;\n            }\n\n            stableEmptyComposerReads = 0;\n            if (!ComposerEvidenceTextEquals(composer.Text, expected))\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n\n            if (readiness.SendButtonPresent && readiness.SendButtonEnabled)\n            {\n                stableStillReadyReads++;\n                if (stableStillReadyReads >= 3)\n                    return ImmediatePhysicalSubmitObservation.ClickNotAccepted;\n            }\n            else\n            {\n                stableStillReadyReads = 0;\n            }\n\n            await Task.Delay(250, cancellationToken);\n        }\n\n        return stableStillReadyReads > 0 || stableEmptyComposerReads > 0\n            ? ImmediatePhysicalSubmitObservation.ClickNotAccepted\n            : ImmediatePhysicalSubmitObservation.Ambiguous;\n    }\n\n"""
    text = text[:start] + observer + text[end:]

    send_start = text.index("    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)")
    verified_start = text.index("    public async Task<bool> SendChatMessageVerifiedAsync", send_start)
    send = text[send_start:verified_start]
    submit_start = send.index("        const string submitExpression =")
    replacement = """        // Use a real CDP input event for the physical submit. Synthetic HTMLElement.click()\n        // can clear ChatGPT's editor without creating a user turn, which is not delivery.\n        var submitted = await TryDispatchNativeSendClickAsync(tab, cancellationToken);\n        if (!submitted) return false;\n        try\n        {\n            await EvaluateAsync(tab, \"(() => { try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch {} return true; })()\", cancellationToken, false);\n        }\n        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))\n        {\n            // Physical input already happened; verified observation owns any transport uncertainty.\n        }\n        return true;\n    }\n\n"""
    send = send[:submit_start] + replacement
    text = text[:send_start] + send + text[verified_start:]

    text = replace_exact(
        text,
        """            if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal))\n            {\n                VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\", \"new-user-turn-observed\", submitAttempts);\n                return true;\n            }\n""",
        """            if (current.Count > before.Count && ComposerEvidenceTextEquals(current.LastText, expected))\n            {\n                VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\", \"new-user-turn-observed\", submitAttempts);\n                return true;\n            }\n""",
        "canonical verified user-turn comparison",
    )

    accepted = """            if (immediateObservation == ImmediatePhysicalSubmitObservation.AcceptedTransition)\n            {\n                submitAttempts++;\n                unacceptedClickAttempts = 0;\n                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;\n                VerifiedSendDiagnostics.Record(\"AwaitingReceipt\", \"physical-submit-transition-observed\", submitAttempts);\n                continue;\n            }\n\n"""
    text = replace_exact(text, accepted, "", "remove empty-composer ambiguous acceptance branch")
    text = text.replace("click-not-accepted-composer-still-ready", "physical-input-not-accepted")
    text = text.replace("physical-click-not-accepted", "physical-input-retry-limit-reached")
    text = text.replace("// The click happened but the UI no longer gives enough evidence", "// Physical input happened but the UI no longer gives enough evidence")
    text = text.replace("physical-submit-ambiguous-after-click", "physical-submit-ambiguous-after-input")

    if "sendButton.click();" in text[send_start:verified_start]:
        raise RuntimeError("Synthetic sendButton.click() remains in SendChatMessageAsync")
    if "ImmediatePhysicalSubmitObservation.AcceptedTransition" in text:
        raise RuntimeError("AcceptedTransition remains in verified-send source")
    if "composer-cleared-without-user-turn" not in text:
        raise RuntimeError("Empty composer field regression guard missing")
    return text


rw(CHROME, patch_chrome)

for relative in [
    "src/GPTDeskTop/GPTDeskTop.csproj",
    "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
    "src/GPTDeskTop.Setup/Program.cs",
    "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
]:
    path = ROOT / relative
    rw(path, lambda t: t.replace("2.0.4", "2.0.5"))


def patch_physical_tests(text: str) -> str:
    text = text.replace("JavascriptClickAloneIsNotTreatedAsPhysicalSubmitAcceptance", "SyntheticDomClickIsNotUsedForPhysicalSubmission")
    text = text.replace("click-not-accepted-composer-still-ready", "physical-input-not-accepted")
    text = text.replace("physical-click-not-accepted", "physical-input-retry-limit-reached")
    text = text.replace("physical-submit-ambiguous-after-click", "physical-submit-ambiguous-after-input")
    text = text.replace(
        """        Assert.Contains(\"composer.Text.Length == 0\", observer, StringComparison.Ordinal);\n        Assert.Contains(\"readiness.SendButtonPresent && readiness.SendButtonEnabled\", observer, StringComparison.Ordinal);\n""",
        """        Assert.Contains(\"composer.Text.Length == 0\", observer, StringComparison.Ordinal);\n        Assert.Contains(\"stableEmptyComposerReads >= 8\", observer, StringComparison.Ordinal);\n        Assert.Contains(\"composer-cleared-without-user-turn\", observer, StringComparison.Ordinal);\n        Assert.Contains(\"readiness.SendButtonPresent && readiness.SendButtonEnabled\", observer, StringComparison.Ordinal);\n""",
    )
    start = text.index("    [Fact]\n    public void RejectedDomClickUsesNativeCdpPointerFallbackOnlyAfterStableUnchangedComposerEvidence()")
    end = text.index("    [Fact]\n    public void ComposerEvidenceComparisonCanonicalizesRichEditorWhitespaceWithoutIgnoringContentChanges()", start)
    test = """    [Fact]\n    public void PhysicalSubmissionUsesNativeCdpPointerDirectly()\n    {\n        var source = ChromeSource();\n        var send = Slice(source, \"public async Task<bool> SendChatMessageAsync\", \"public async Task<bool> SendChatMessageVerifiedAsync\");\n\n        Assert.Contains(\"TryDispatchNativeSendClickAsync(tab, cancellationToken)\", send, StringComparison.Ordinal);\n        Assert.DoesNotContain(\"sendButton.click();\", send, StringComparison.Ordinal);\n        Assert.Contains(\"Input.dispatchMouseEvent\", source, StringComparison.Ordinal);\n        Assert.Contains(\"NativeSendClickDispatched\", source, StringComparison.Ordinal);\n    }\n\n"""
    text = text[:start] + test + text[end:]
    text = text.replace("ReleaseIdentityIsV204IncludingInstallerRegistryVersion", "ReleaseIdentityIsV205IncludingInstallerRegistryVersion")
    text = text.replace("2.0.4", "2.0.5")
    return text

rw(PHYSICAL_TESTS, patch_physical_tests)
rw(OPERATOR_TESTS, lambda t: t.replace("2.0.4", "2.0.5"))

# Hard fail if the release identity or field fix did not actually land in the working tree.
if "<Version>2.0.5</Version>" not in (ROOT / "src/GPTDeskTop/GPTDeskTop.csproj").read_text(encoding="utf-8"):
    raise RuntimeError("v2.0.5 application identity was not written")
chrome = CHROME.read_text(encoding="utf-8")
if "composer-cleared-without-user-turn" not in chrome or "sendButton.click();" in chrome[chrome.index("public async Task<bool> SendChatMessageAsync"):chrome.index("public async Task<bool> SendChatMessageVerifiedAsync")]:
    raise RuntimeError("v2.0.5 physical-submit fix was not written")

print("v2.0.5 deterministic verified-submit patch written")
