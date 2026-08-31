from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHROME = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


s = read(CHROME)
if "using System.Text;" not in s:
    s = s.replace("using System.Runtime.InteropServices;\n", "using System.Runtime.InteropServices;\nusing System.Text;\n", 1)

s = replace_once(
    s,
    '''    private async Task<bool> ComposerEditorMatchesExpectedAsync(\n        ChromeTab tab,\n        string expected,\n        CancellationToken cancellationToken)\n    {\n        var expectedLiteral = JsonSerializer.Serialize(expected.Trim());\n        var expression = $$\"\"\"\n        (() => {\n          const expected = {{expectedLiteral}};\n          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');\n          if (!editor) return false;\n          const text = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement\n            ? editor.value\n            : (editor.innerText || editor.textContent || '');\n          return (text || '').trim() === expected;\n        })()\n        \"\"\";\n        var value = await EvaluateAsync(tab, expression, cancellationToken, false);\n        return value.ValueKind == JsonValueKind.True;\n    }\n\n''',
    '''    private async Task<bool> ComposerEditorMatchesExpectedAsync(\n        ChromeTab tab,\n        string expected,\n        CancellationToken cancellationToken)\n    {\n        var composer = await ReadComposerTextAsync(tab, cancellationToken);\n        return composer.Present && ComposerEvidenceTextEquals(composer.Text, expected);\n    }\n\n    private static string CanonicalizeComposerEvidenceText(string? text)\n    {\n        if (string.IsNullOrWhiteSpace(text)) return string.Empty;\n\n        var normalized = text\n            .Replace(\"\\r\\n\", \"\\n\", StringComparison.Ordinal)\n            .Replace('\\r', '\\n')\n            .Replace('\\u00a0', ' ');\n        var builder = new StringBuilder(normalized.Length);\n        var pendingWhitespace = false;\n        foreach (var character in normalized)\n        {\n            if (char.IsWhiteSpace(character) || character is '\\u200b' or '\\ufeff')\n            {\n                pendingWhitespace = builder.Length > 0;\n                continue;\n            }\n\n            if (pendingWhitespace)\n            {\n                builder.Append(' ');\n                pendingWhitespace = false;\n            }\n            builder.Append(character);\n        }\n\n        return builder.ToString().Trim();\n    }\n\n    private static bool ComposerEvidenceTextEquals(string? actual, string? expected)\n        => string.Equals(\n            CanonicalizeComposerEvidenceText(actual),\n            CanonicalizeComposerEvidenceText(expected),\n            StringComparison.Ordinal);\n\n''',
    "composer evidence canonicalization",
)

s = replace_once(
    s,
    '''            if (!string.Equals(composer.Text, expected, StringComparison.Ordinal))\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n''',
    '''            if (!ComposerEvidenceTextEquals(composer.Text, expected))\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n''',
    "immediate observer canonical composer comparison",
)

marker = '''    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)\n'''
helper = '''    private async Task<bool> TryDispatchNativeSendClickAsync(ChromeTab tab, CancellationToken cancellationToken)\n    {\n        const string expression = \"\"\"\n        (() => {\n          const visible = element => {\n            if (!element) return false;\n            const rect = element.getBoundingClientRect();\n            const style = getComputedStyle(element);\n            return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';\n          };\n          const stop = document.querySelector('button[data-testid=\"stop-button\"]');\n          if (visible(stop)) return { ready: false, x: 0, y: 0 };\n          const sendButton = document.querySelector('button[data-testid=\"send-button\"]') ||\n            [...document.querySelectorAll('button')].find(button => {\n              if (!visible(button)) return false;\n              const label = button.getAttribute('aria-label') || '';\n              return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());\n            });\n          if (!sendButton || sendButton.disabled || sendButton.getAttribute('aria-disabled') === 'true' || !visible(sendButton))\n            return { ready: false, x: 0, y: 0 };\n          const rect = sendButton.getBoundingClientRect();\n          return { ready: true, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };\n        })()\n        \"\"\";\n\n        var target = await EvaluateAsync(tab, expression, cancellationToken, false);\n        if (!target.TryGetProperty(\"ready\", out var ready) || !ready.GetBoolean()) return false;\n        var x = target.TryGetProperty(\"x\", out var xElement) ? xElement.GetDouble() : double.NaN;\n        var y = target.TryGetProperty(\"y\", out var yElement) ? yElement.GetDouble() : double.NaN;\n        if (double.IsNaN(x) || double.IsNaN(y)) return false;\n\n        await SendCommandAsync(tab, \"Input.dispatchMouseEvent\", new { type = \"mouseMoved\", x, y, button = \"none\", buttons = 0 }, cancellationToken);\n        await SendCommandAsync(tab, \"Input.dispatchMouseEvent\", new { type = \"mousePressed\", x, y, button = \"left\", buttons = 1, clickCount = 1 }, cancellationToken);\n        await SendCommandAsync(tab, \"Input.dispatchMouseEvent\", new { type = \"mouseReleased\", x, y, button = \"left\", buttons = 0, clickCount = 1 }, cancellationToken);\n        RuntimeFlightRecorder.Record(\"Composer\", \"NativeSendClickDispatched\", \"submitted\", \"cdp-input\", tabId: tab.Id, conversationRef: tab.Url);\n        return true;\n    }\n\n    private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync(\n        ChromeTab tab,\n        string expected,\n        CancellationToken cancellationToken)\n    {\n        var stableStillReadyReads = 0;\n        for (var attempt = 0; attempt < 4; attempt++)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            await Task.Delay(200, cancellationToken);\n            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n            if (readiness.HasRenderedError || readiness.IsGenerating) return false;\n            var composer = await ReadComposerTextAsync(tab, cancellationToken);\n            if (!composer.Present || composer.Text.Length == 0) return false;\n            if (!ComposerEvidenceTextEquals(composer.Text, expected)) return false;\n            if (readiness.SendButtonPresent && readiness.SendButtonEnabled)\n                stableStillReadyReads++;\n            else\n                stableStillReadyReads = 0;\n\n            if (stableStillReadyReads >= 3)\n            {\n                VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"dom-click-rejected-native-input-fallback\", 0);\n                return await TryDispatchNativeSendClickAsync(tab, cancellationToken);\n            }\n        }\n\n        return false;\n    }\n\n'''
if "private async Task<bool> TryDispatchNativeSendClickAsync" not in s:
    if s.count(marker) != 1:
        raise RuntimeError("native fallback insertion marker missing")
    s = s.replace(marker, helper + marker, 1)

s = replace_once(
    s,
    '''        var submitted = await EvaluateAsync(tab, submitExpression, cancellationToken, false);\n        return submitted.ValueKind == JsonValueKind.True;\n    }\n''',
    '''        var submitted = await EvaluateAsync(tab, submitExpression, cancellationToken, false);\n        if (submitted.ValueKind != JsonValueKind.True) return false;\n\n        // Runtime.evaluate .click() only proves that JavaScript invoked HTMLElement.click().\n        // ChatGPT can leave the same text and enabled Send button untouched, which is positive\n        // evidence that no user turn was accepted. In that narrow state, dispatch one real CDP\n        // pointer click before handing control back to the exactly-once verifier.\n        await TryNativeFallbackAfterRejectedDomClickAsync(tab, message, cancellationToken);\n        return true;\n    }\n''',
    "native pointer fallback call",
)

s = replace_once(
    s,
    '''        var stableAbsenceReads = 0;\n        var stableUnexpectedReads = 0;\n''',
    '''        var stableAbsenceReads = 0;\n        var stableReadyComposerReads = 0;\n        var stableUnexpectedReads = 0;\n''',
    "stable ready composer reconciliation counter",
)

s = replace_once(
    s,
    '''            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n            var observation = MonitorDeliveryRecoveryPolicy.ClassifyPostRefreshUserTurn(\n''',
    '''            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);\n            if (receiptAfterRefresh.Success\n                && receiptAfterRefresh.Count > baselineUserTurnCount\n                && string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal))\n                return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;\n\n            try\n            {\n                var composerReadiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n                var composer = await ReadComposerTextAsync(tab, cancellationToken);\n                if (!composerReadiness.IsGenerating\n                    && !composerReadiness.HasRenderedError\n                    && composerReadiness.EditorPresent\n                    && composerReadiness.EditorEnabled\n                    && composerReadiness.SendButtonPresent\n                    && composerReadiness.SendButtonEnabled\n                    && composer.Present\n                    && ComposerEvidenceTextEquals(composer.Text, expected))\n                {\n                    stableReadyComposerReads++;\n                    if (stableReadyComposerReads >= 3)\n                    {\n                        VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"stable-composer-proves-submit-not-accepted\", 0);\n                        return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;\n                    }\n                }\n                else\n                {\n                    stableReadyComposerReads = 0;\n                }\n            }\n            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))\n            {\n                stableReadyComposerReads = 0;\n            }\n\n            var observation = MonitorDeliveryRecoveryPolicy.ClassifyPostRefreshUserTurn(\n''',
    "reconciliation composer rejection evidence",
)

write(CHROME, s)

for relative in [
    "src/GPTDeskTop/GPTDeskTop.csproj",
    "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
    "src/GPTDeskTop.Setup/Program.cs",
    "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
    "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs",
    "tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs",
]:
    path = ROOT / relative
    text = read(path)
    if "2.0.3" in text:
        write(path, text.replace("2.0.3", "2.0.4"))

pt = ROOT / "tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs"
t = read(pt)
if "RejectedDomClickUsesNativeCdpPointerFallbackOnlyAfterStableUnchangedComposerEvidence" not in t:
    addition = r'''
    [Fact]
    public void RejectedDomClickUsesNativeCdpPointerFallbackOnlyAfterStableUnchangedComposerEvidence()
    {
        var source = ChromeSource();
        var send = Slice(source, "public async Task<bool> SendChatMessageAsync", "public async Task<bool> SendChatMessageVerifiedAsync");
        var helper = Slice(source, "private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync", "private async Task<bool> RefreshStuckComposerAsync");

        Assert.Contains("TryNativeFallbackAfterRejectedDomClickAsync(tab, message, cancellationToken)", send, StringComparison.Ordinal);
        Assert.Contains("stableStillReadyReads >= 3", helper, StringComparison.Ordinal);
        Assert.Contains("ComposerEvidenceTextEquals(composer.Text, expected)", helper, StringComparison.Ordinal);
        Assert.Contains("TryDispatchNativeSendClickAsync", helper, StringComparison.Ordinal);
        Assert.Contains("Input.dispatchMouseEvent", source, StringComparison.Ordinal);
        Assert.Contains("NativeSendClickDispatched", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerEvidenceComparisonCanonicalizesRichEditorWhitespaceWithoutIgnoringContentChanges()
    {
        var source = ChromeSource();
        var helper = Slice(source, "private static string CanonicalizeComposerEvidenceText", "private enum ImmediatePhysicalSubmitObservation");

        Assert.Contains("char.IsWhiteSpace", helper, StringComparison.Ordinal);
        Assert.Contains("\\u200b", helper, StringComparison.Ordinal);
        Assert.Contains("ComposerEvidenceTextEquals", helper, StringComparison.Ordinal);
        var observer = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> TryDispatchNativeSendClickAsync");
        Assert.Contains("ComposerEvidenceTextEquals(composer.Text, expected)", observer, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationAuthorizesRetryWhenComposerItselfStablyProvesSubmitWasNotAccepted()
    {
        var source = ChromeSource();
        var reconcile = Slice(source, "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync", "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("stableReadyComposerReads", reconcile, StringComparison.Ordinal);
        Assert.Contains("stableReadyComposerReads >= 3", reconcile, StringComparison.Ordinal);
        Assert.Contains("stable-composer-proves-submit-not-accepted", reconcile, StringComparison.Ordinal);
        Assert.Contains("ComposerEvidenceTextEquals(composer.Text, expected)", reconcile, StringComparison.Ordinal);
        Assert.Contains("UnacknowledgedSubmitReconciliationResult.RetryAuthorized", reconcile, StringComparison.Ordinal);
    }

'''
    marker = "    [Fact]\n    public void GlobalRateLimitModalBlocksComposerSubmission()\n"
    if marker not in t:
        raise RuntimeError("physical-submit test insertion marker missing")
    t = t.replace(marker, addition + marker, 1)
    t = t.replace("ReleaseIdentityIsV203IncludingInstallerRegistryVersion", "ReleaseIdentityIsV204IncludingInstallerRegistryVersion")
    write(pt, t)

print("v2.0.4 native physical-submit fix applied")
