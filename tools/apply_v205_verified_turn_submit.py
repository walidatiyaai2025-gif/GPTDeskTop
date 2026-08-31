from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHROME = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
PHYSICAL_TESTS = ROOT / "tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs"
OPERATOR_TESTS = ROOT / "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs"


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

s = replace_once(
    s,
    '''    private enum ImmediatePhysicalSubmitObservation
    {
        ReceiptConfirmed,
        AcceptedTransition,
        ClickNotAccepted,
        Ambiguous
    }
''',
    '''    private enum ImmediatePhysicalSubmitObservation
    {
        ReceiptConfirmed,
        ClickNotAccepted,
        Ambiguous
    }

    private enum PhysicalSubmitMode
    {
        NativeKeyboard,
        NativePointer
    }
''',
    "physical submit mode enum",
)

observer_start = s.index("    private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync")
observer_end = s.index("    private async Task<bool> TryDispatchNativeSendClickAsync", observer_start)
observer = '''    private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync(
        ChromeTab tab,
        string expected,
        int baselineUserTurnCount,
        CancellationToken cancellationToken)
    {
        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(4);
        var stableStillReadyReads = 0;
        var stableEmptyComposerReads = 0;

        while (DateTimeOffset.UtcNow < observationDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (snapshot.Success && snapshot.Count > baselineUserTurnCount)
            {
                if (ComposerEvidenceTextEquals(snapshot.LastText, expected))
                    return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;

                // A different user turn appeared after our physical action. Do not retry blindly.
                return ImmediatePhysicalSubmitObservation.Ambiguous;
            }

            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
            if (readiness.HasRenderedError)
                return ImmediatePhysicalSubmitObservation.Ambiguous;
            if (readiness.IsGenerating)
                return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;

            var composer = await ReadComposerTextAsync(tab, cancellationToken);
            if (!composer.Present)
                return ImmediatePhysicalSubmitObservation.Ambiguous;

            if (composer.Text.Length == 0)
            {
                stableStillReadyReads = 0;
                if (readiness.EditorPresent && readiness.EditorEnabled)
                {
                    stableEmptyComposerReads++;
                    if (stableEmptyComposerReads >= 8)
                    {
                        VerifiedSendDiagnostics.Record("RetryAuthorized", "composer-cleared-without-user-turn", 0);
                        return ImmediatePhysicalSubmitObservation.ClickNotAccepted;
                    }
                }
                else
                {
                    stableEmptyComposerReads = 0;
                }

                await Task.Delay(250, cancellationToken);
                continue;
            }

            stableEmptyComposerReads = 0;
            if (!ComposerEvidenceTextEquals(composer.Text, expected))
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

            await Task.Delay(250, cancellationToken);
        }

        return stableStillReadyReads > 0 || stableEmptyComposerReads > 0
            ? ImmediatePhysicalSubmitObservation.ClickNotAccepted
            : ImmediatePhysicalSubmitObservation.Ambiguous;
    }

'''
s = s[:observer_start] + observer + s[observer_end:]

pointer_marker = "    private async Task<bool> TryDispatchNativeSendClickAsync(ChromeTab tab, CancellationToken cancellationToken)\n"
enter_helper = '''    private async Task<bool> TryDispatchNativeEnterAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        await SendCommandAsync(tab, "Input.dispatchKeyEvent", new
        {
            type = "rawKeyDown",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13
        }, cancellationToken);
        await SendCommandAsync(tab, "Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13
        }, cancellationToken);
        RuntimeFlightRecorder.Record("Composer", "NativeSendEnterDispatched", "submitted", "cdp-input", tabId: tab.Id, conversationRef: tab.Url);
        return true;
    }

'''
if "private async Task<bool> TryDispatchNativeEnterAsync" not in s:
    if s.count(pointer_marker) != 1:
        raise RuntimeError("native Enter insertion marker missing")
    s = s.replace(pointer_marker, enter_helper + pointer_marker, 1)

fallback_start = s.find("    private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync")
if fallback_start >= 0:
    fallback_end = s.index("    private async Task<bool> RefreshStuckComposerAsync", fallback_start)
    s = s[:fallback_start] + s[fallback_end:]

send_start = s.index("    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)")
send_end = s.index("    public async Task<bool> SendChatMessageVerifiedAsync", send_start)
send_core = '''    public Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
        => SendChatMessageCoreAsync(tab, message, PhysicalSubmitMode.NativeKeyboard, cancellationToken);

    private async Task<bool> SendChatMessageCoreAsync(
        ChromeTab tab,
        string message,
        PhysicalSubmitMode submitMode,
        CancellationToken cancellationToken)
    {
        var preparationDecision = await ReadComposerDecisionAsync(tab, requireSendReady: false, cancellationToken);
        if (preparationDecision != ComposerAutomationDecision.ReadyToPrepare)
            return false;

        var textLiteral = JsonSerializer.Serialize(message);
        var setEditorExpression = $$"""
        (() => {
          const text = {{textLiteral}};
          const visible = element => {
            if (!element) return false;
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
          };
          const stop = document.querySelector('button[data-testid="stop-button"]');
          if (visible(stop)) return false;
          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
          if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;
          editor.focus();
          if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) {
            const setter = Object.getOwnPropertyDescriptor(editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')?.set;
            setter?.call(editor, text);
            editor.dispatchEvent(new Event('input', { bubbles: true }));
            editor.dispatchEvent(new Event('change', { bubbles: true }));
          } else {
            const selection = window.getSelection();
            const range = document.createRange();
            range.selectNodeContents(editor);
            selection?.removeAllRanges();
            selection?.addRange(range);
            document.execCommand('insertText', false, text);
            editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));
          }
          return true;
        })()
        """;

        var editorPrepared = await EvaluateAsync(tab, setEditorExpression, cancellationToken, false);
        if (editorPrepared.ValueKind != JsonValueKind.True) return false;

        for (var readinessAttempt = 0; readinessAttempt < 8; readinessAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var submitDecision = await ReadComposerDecisionAsync(tab, requireSendReady: true, cancellationToken);
            if (submitDecision == ComposerAutomationDecision.ReadyToSend)
                break;
            if (submitDecision is ComposerAutomationDecision.DeferWhileGenerating or ComposerAutomationDecision.DeferForRenderedError)
                return false;
            if (readinessAttempt == 7) return false;
            await Task.Delay(150, cancellationToken);
        }

        var dispatched = submitMode == PhysicalSubmitMode.NativePointer
            ? await TryDispatchNativeSendClickAsync(tab, cancellationToken)
            : await TryDispatchNativeEnterAsync(tab, cancellationToken);
        if (dispatched)
        {
            try
            {
                await EvaluateAsync(tab, "(() => { try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch {} return true; })()", cancellationToken, false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                // Physical input already happened; verified observation owns the uncertainty.
            }
        }
        return dispatched;
    }

'''
s = s[:send_start] + send_core + s[send_end:]

s = replace_once(
    s,
    '''            if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "new-user-turn-observed", submitAttempts);
                return true;
            }
''',
    '''            if (current.Count > before.Count && ComposerEvidenceTextEquals(current.LastText, expected))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "new-user-turn-observed", submitAttempts);
                return true;
            }
''',
    "canonical verified receipt comparison",
)

s = replace_once(
    s,
    "                submitted = await SendChatMessageAsync(tab, message, cancellationToken);\n",
    '''                var submitMode = ((submitAttempts + unacceptedClickAttempts) & 1) == 0
                    ? PhysicalSubmitMode.NativeKeyboard
                    : PhysicalSubmitMode.NativePointer;
                VerifiedSendDiagnostics.Record("PhysicalSubmit", submitMode == PhysicalSubmitMode.NativeKeyboard ? "native-enter" : "native-pointer", submitAttempts);
                submitted = await SendChatMessageCoreAsync(tab, message, submitMode, cancellationToken);
''',
    "alternating native submit mode",
)

accepted_transition = '''            if (immediateObservation == ImmediatePhysicalSubmitObservation.AcceptedTransition)
            {
                submitAttempts++;
                unacceptedClickAttempts = 0;
                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
                VerifiedSendDiagnostics.Record("AwaitingReceipt", "physical-submit-transition-observed", submitAttempts);
                continue;
            }

'''
if accepted_transition in s:
    s = s.replace(accepted_transition, "", 1)

s = s.replace("click-not-accepted-composer-still-ready", "physical-input-not-accepted-composer-still-ready")
s = s.replace("physical-click-not-accepted", "physical-input-not-accepted")
s = s.replace(
    '''                // No user turn, no generation, and the exact expected text is still sitting in an
                // enabled composer. The previous DOM click did not become a physical submit, so one
                // bounded click retry is safe and does not consume the exactly-once submit budget.
''',
    '''                // No user turn and no generation were observed. Either the same prompt remained ready
                // or the editor cleared locally without producing a user turn. Alternate the native
                // input mechanism on the next bounded retry without consuming exactly-once budget.
''')

s = replace_once(
    s,
    '''        var stableAbsenceReads = 0;
        var stableReadyComposerReads = 0;
        var stableUnexpectedReads = 0;
''',
    '''        var stableAbsenceReads = 0;
        var stableReadyComposerReads = 0;
        var stableEmptyComposerReads = 0;
        var stableUnexpectedReads = 0;
''',
    "empty composer reconciliation counter",
)

s = replace_once(
    s,
    '''        if (receiptBeforeRefresh.Count > baselineUserTurnCount
            && string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal))
            return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
''',
    '''        if (receiptBeforeRefresh.Count > baselineUserTurnCount
            && ComposerEvidenceTextEquals(receiptBeforeRefresh.LastText, expected))
            return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
        if (receiptBeforeRefresh.Count > baselineUserTurnCount)
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;
''',
    "pre-rebind receipt ambiguity guard",
)

s = replace_once(
    s,
    '''            if (receiptAfterRefresh.Success
                && receiptAfterRefresh.Count > baselineUserTurnCount
                && string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal))
                return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
''',
    '''            if (receiptAfterRefresh.Success && receiptAfterRefresh.Count > baselineUserTurnCount)
            {
                if (ComposerEvidenceTextEquals(receiptAfterRefresh.LastText, expected))
                    return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
                return UnacknowledgedSubmitReconciliationResult.Ambiguous;
            }
''',
    "post-rebind receipt ambiguity guard",
)

s = replace_once(
    s,
    '''                if (!composerReadiness.IsGenerating
                    && !composerReadiness.HasRenderedError
                    && composerReadiness.EditorPresent
                    && composerReadiness.EditorEnabled
                    && composerReadiness.SendButtonPresent
                    && composerReadiness.SendButtonEnabled
                    && composer.Present
                    && ComposerEvidenceTextEquals(composer.Text, expected))
                {
                    stableReadyComposerReads++;
                    if (stableReadyComposerReads >= 3)
                    {
                        VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-composer-proves-submit-not-accepted", 0);
                        return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;
                    }
                }
                else
                {
                    stableReadyComposerReads = 0;
                }
''',
    '''                if (!composerReadiness.IsGenerating
                    && !composerReadiness.HasRenderedError
                    && composerReadiness.EditorPresent
                    && composerReadiness.EditorEnabled
                    && composer.Present)
                {
                    if (composer.Text.Length == 0)
                    {
                        stableReadyComposerReads = 0;
                        stableEmptyComposerReads++;
                        if (stableEmptyComposerReads >= 6)
                        {
                            VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-empty-composer-without-user-turn", 0);
                            return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;
                        }
                    }
                    else if (composerReadiness.SendButtonPresent
                             && composerReadiness.SendButtonEnabled
                             && ComposerEvidenceTextEquals(composer.Text, expected))
                    {
                        stableEmptyComposerReads = 0;
                        stableReadyComposerReads++;
                        if (stableReadyComposerReads >= 3)
                        {
                            VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-composer-proves-submit-not-accepted", 0);
                            return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;
                        }
                    }
                    else
                    {
                        stableReadyComposerReads = 0;
                        stableEmptyComposerReads = 0;
                    }
                }
                else
                {
                    stableReadyComposerReads = 0;
                    stableEmptyComposerReads = 0;
                }
''',
    "empty composer reconciliation evidence",
)

s = s.replace(
    '''                stableAbsenceReads = 0;
                stableUnexpectedReads = 0;
''',
    '''                stableAbsenceReads = 0;
                stableReadyComposerReads = 0;
                stableEmptyComposerReads = 0;
                stableUnexpectedReads = 0;
''')
s = s.replace(
    '''                stableAbsenceReads = 0;
                if (receiptAfterRefresh.Count == lastUnexpectedCount
''',
    '''                stableAbsenceReads = 0;
                stableReadyComposerReads = 0;
                stableEmptyComposerReads = 0;
                if (receiptAfterRefresh.Count == lastUnexpectedCount
''',
    1)

write(CHROME, s)

for relative in [
    "src/GPTDeskTop/GPTDeskTop.csproj",
    "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
    "src/GPTDeskTop.Setup/Program.cs",
    "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
]:
    path = ROOT / relative
    text = read(path)
    write(path, text.replace("2.0.4", "2.0.5"))

pt = read(PHYSICAL_TESTS)
pt = pt.replace("JavascriptClickAloneIsNotTreatedAsPhysicalSubmitAcceptance", "SyntheticDomClickIsNotUsedForPhysicalSubmission")
pt = pt.replace("click-not-accepted-composer-still-ready", "physical-input-not-accepted-composer-still-ready")
pt = pt.replace("physical-click-not-accepted", "physical-input-not-accepted")
pt = pt.replace(
    '''        Assert.Contains("composer.Text.Length == 0", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent && readiness.SendButtonEnabled", observer, StringComparison.Ordinal);
''',
    '''        Assert.Contains("composer.Text.Length == 0", observer, StringComparison.Ordinal);
        Assert.Contains("stableEmptyComposerReads >= 8", observer, StringComparison.Ordinal);
        Assert.Contains("composer-cleared-without-user-turn", observer, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent && readiness.SendButtonEnabled", observer, StringComparison.Ordinal);
''')
old_test_start = pt.find("    [Fact]\n    public void RejectedDomClickUsesNativeCdpPointerFallbackOnlyAfterStableUnchangedComposerEvidence()")
if old_test_start >= 0:
    old_test_end = pt.index("    [Fact]\n    public void ComposerEvidenceComparisonCanonicalizesRichEditorWhitespaceWithoutIgnoringContentChanges()", old_test_start)
    new_test = '''    [Fact]
    public void PhysicalSubmitUsesNativeKeyboardFirstAndNativePointerOnBoundedRetry()
    {
        var source = ChromeSource();
        var core = Slice(source, "private async Task<bool> SendChatMessageCoreAsync", "public async Task<bool> SendChatMessageVerifiedAsync");
        var verified = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");

        Assert.Contains("Input.dispatchKeyEvent", source, StringComparison.Ordinal);
        Assert.Contains("NativeSendEnterDispatched", source, StringComparison.Ordinal);
        Assert.Contains("Input.dispatchMouseEvent", source, StringComparison.Ordinal);
        Assert.Contains("NativeSendClickDispatched", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sendButton.click()", core, StringComparison.Ordinal);
        Assert.Contains("((submitAttempts + unacceptedClickAttempts) & 1) == 0", verified, StringComparison.Ordinal);
        Assert.Contains("PhysicalSubmitMode.NativeKeyboard", verified, StringComparison.Ordinal);
        Assert.Contains("PhysicalSubmitMode.NativePointer", verified, StringComparison.Ordinal);
    }

'''
    pt = pt[:old_test_start] + new_test + pt[old_test_end:]
pt = pt.replace(
    'var observer = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> TryDispatchNativeSendClickAsync");',
    'var observer = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> TryDispatchNativeEnterAsync");')
pt = pt.replace(
    '''        Assert.Contains("stableReadyComposerReads >= 3", reconcile, StringComparison.Ordinal);
        Assert.Contains("stable-composer-proves-submit-not-accepted", reconcile, StringComparison.Ordinal);
''',
    '''        Assert.Contains("stableReadyComposerReads >= 3", reconcile, StringComparison.Ordinal);
        Assert.Contains("stable-composer-proves-submit-not-accepted", reconcile, StringComparison.Ordinal);
        Assert.Contains("stableEmptyComposerReads >= 6", reconcile, StringComparison.Ordinal);
        Assert.Contains("stable-empty-composer-without-user-turn", reconcile, StringComparison.Ordinal);
''')
pt = pt.replace("ReleaseIdentityIsV203IncludingInstallerRegistryVersion", "ReleaseIdentityIsV205IncludingInstallerRegistryVersion")
pt = pt.replace("2.0.4", "2.0.5")
write(PHYSICAL_TESTS, pt)

operator = read(OPERATOR_TESTS).replace("2.0.4", "2.0.5")
write(OPERATOR_TESTS, operator)

print("v2.0.5 verified-turn submit hotfix applied")
