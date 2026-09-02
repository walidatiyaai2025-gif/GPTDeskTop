from pathlib import Path

root = Path(__file__).resolve().parents[1]
chrome_path = root / 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
monitor_path = root / 'src/GPTDeskTop/Services/ChatGptMonitorService.cs'
readiness_path = root / 'src/GPTDeskTop/Services/ChatComposerReadinessScript.cs'

chrome = chrome_path.read_text(encoding='utf-8')
monitor = monitor_path.read_text(encoding='utf-8')
readiness = readiness_path.read_text(encoding='utf-8')

old_selector = '''          const sendButton = document.querySelector('button[data-testid="send-button"]') ||
            [...document.querySelectorAll('button')].find(button => {
              if (!visible(button)) return false;
              const label = button.getAttribute('aria-label') || '';
              return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());
            });
          if (!sendButton || sendButton.disabled || sendButton.getAttribute('aria-disabled') === 'true' || !visible(sendButton))
            return { ready: false, x: 0, y: 0 };'''
new_selector = '''          const sendButton = [...document.querySelectorAll('button')].find(button => {
            if (!visible(button) || button.disabled || button.getAttribute('aria-disabled') === 'true') return false;
            if (button.getAttribute('data-testid') === 'send-button') return true;
            const label = button.getAttribute('aria-label') || '';
            return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());
          });
          if (!sendButton)
            return { ready: false, x: 0, y: 0 };'''
if old_selector not in chrome:
    raise SystemExit('native send selector anchor missing')
chrome = chrome.replace(old_selector, new_selector, 1)

readiness_old = '''  const send = document.querySelector('button[data-testid="send-button"]') ||
    [...document.querySelectorAll('button')].find(button => {
      if (!visible(button)) return false;
      const label = (button.getAttribute('aria-label') || '').trim();
      return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label);
    });'''
readiness_new = '''  const send = [...document.querySelectorAll('button')].find(button => {
    if (!visible(button) || button.disabled || button.getAttribute('aria-disabled') === 'true') return false;
    if (button.getAttribute('data-testid') === 'send-button') return true;
    const label = (button.getAttribute('aria-label') || '').trim();
    return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label);
  });'''
if readiness_old not in readiness:
    raise SystemExit('readiness selector anchor missing')
readiness = readiness.replace(readiness_old, readiness_new, 1)

native_click_end = '''        RuntimeFlightRecorder.Record("Composer", "NativeSendClickDispatched", "submitted", "cdp-input", tabId: tab.Id, conversationRef: tab.Url);
        return true;
    }

    private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync'''
native_enter = '''        RuntimeFlightRecorder.Record("Composer", "NativeSendClickDispatched", "submitted", "cdp-input", tabId: tab.Id, conversationRef: tab.Url);
        return true;
    }

    private async Task<bool> TryDispatchNativeEnterSubmitAsync(
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken)
    {
        var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
        if (readiness.IsGenerating
            || readiness.HasRenderedError
            || !readiness.EditorPresent
            || !readiness.EditorEnabled
            || !readiness.SendButtonPresent
            || !readiness.SendButtonEnabled)
            return false;

        if (!await ComposerEditorMatchesExpectedAsync(tab, expected, cancellationToken))
            return false;

        const string focusExpression = """
        (() => {
          const visible = element => {
            if (!element) return false;
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
          };
          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
          if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;
          editor.focus();
          return document.activeElement === editor || editor.contains(document.activeElement);
        })()
        """;
        var focused = await EvaluateAsync(tab, focusExpression, cancellationToken, false);
        if (focused.ValueKind != JsonValueKind.True)
            return false;

        await SendCommandAsync(tab, "Input.dispatchKeyEvent", new
        {
            type = "rawKeyDown",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13,
            modifiers = 0
        }, cancellationToken);
        await SendCommandAsync(tab, "Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            key = "Enter",
            code = "Enter",
            windowsVirtualKeyCode = 13,
            nativeVirtualKeyCode = 13,
            modifiers = 0
        }, cancellationToken);
        RuntimeFlightRecorder.Record("Composer", "NativeEnterSubmitDispatched", "submitted", "cdp-key-input", tabId: tab.Id, conversationRef: tab.Url);
        return true;
    }

    public async Task<bool> IsComposerDefinitelyStillAwaitingSubmitAsync(
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken = default)
    {
        const int confirmationsRequired = 3;
        var confirmations = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                var exactComposer = await ComposerEditorMatchesExpectedAsync(tab, expected, cancellationToken);
                if (!readiness.IsGenerating
                    && !readiness.HasRenderedError
                    && readiness.EditorPresent
                    && readiness.EditorEnabled
                    && readiness.SendButtonPresent
                    && readiness.SendButtonEnabled
                    && exactComposer)
                {
                    confirmations++;
                    if (confirmations >= confirmationsRequired)
                    {
                        VerifiedSendDiagnostics.Record("RetryAuthorized", "confirmed-unsent-composer", 0);
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                return false;
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync'''
if native_click_end not in chrome:
    raise SystemExit('native click insertion anchor missing')
chrome = chrome.replace(native_click_end, native_enter, 1)

click_reject_old = '''            if (immediateObservation == ImmediatePhysicalSubmitObservation.ClickNotAccepted)
            {
                unacceptedClickAttempts++;
                VerifiedSendDiagnostics.Record("RetryAuthorized", "physical-input-not-accepted", submitAttempts);
                if (unacceptedClickAttempts >= maxUnacceptedClickAttempts)
                {
                    VerifiedSendDiagnostics.Record("FailedClosed", "physical-input-retry-limit-reached", submitAttempts);
                    return false;
                }

                // No user turn, no generation, and the exact expected text is still sitting in an
                // enabled composer. The previous DOM click did not become a physical submit, so one
                // bounded click retry is safe and does not consume the exactly-once submit budget.
                await Task.Delay(350, cancellationToken);
                continue;
            }'''
click_reject_new = '''            if (immediateObservation == ImmediatePhysicalSubmitObservation.ClickNotAccepted)
            {
                unacceptedClickAttempts++;
                VerifiedSendDiagnostics.Record("RetryAuthorized", "physical-input-not-accepted", submitAttempts);

                // Field evidence can show a fully populated, enabled composer after CDP mouse input.
                // That is authoritative non-delivery evidence. Before another mouse retry, use one
                // trusted native Enter path while the exact expected text is still present. This is
                // safe because ClickNotAccepted proves no user turn/generation was created.
                if (unacceptedClickAttempts == 1
                    && await TryDispatchNativeEnterSubmitAsync(tab, expected, cancellationToken))
                {
                    ImmediatePhysicalSubmitObservation enterObservation;
                    try
                    {
                        enterObservation = await ObserveImmediatePhysicalSubmitAsync(
                            tab,
                            expected,
                            before.Count,
                            cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
                    {
                        submitAttempts++;
                        unacceptedClickAttempts = 0;
                        unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
                        _sessionPool.Invalidate(tab.Id);
                        VerifiedSendDiagnostics.Record("AwaitingReceipt", "post-enter-observation-unreadable", submitAttempts);
                        await Task.Delay(250, cancellationToken);
                        continue;
                    }

                    if (enterObservation == ImmediatePhysicalSubmitObservation.ReceiptConfirmed)
                    {
                        submitAttempts++;
                        VerifiedSendDiagnostics.Record("ReceiptConfirmed", "native-enter-submit-confirmed", submitAttempts);
                        return true;
                    }

                    if (enterObservation == ImmediatePhysicalSubmitObservation.Ambiguous)
                    {
                        submitAttempts++;
                        unacceptedClickAttempts = 0;
                        unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
                        VerifiedSendDiagnostics.Record("AwaitingReceipt", "native-enter-submit-ambiguous", submitAttempts);
                        continue;
                    }

                    VerifiedSendDiagnostics.Record("RetryAuthorized", "native-enter-not-accepted", submitAttempts);
                }

                if (unacceptedClickAttempts >= maxUnacceptedClickAttempts)
                {
                    VerifiedSendDiagnostics.Record("FailedClosed", "physical-input-retry-limit-reached", submitAttempts);
                    return false;
                }

                // No user turn, no generation, and the exact expected text is still sitting in an
                // enabled composer. The previous physical input did not become a submit, so a bounded
                // retry is safe and does not consume the exactly-once submit budget.
                await Task.Delay(350, cancellationToken);
                continue;
            }'''
if click_reject_old not in chrome:
    raise SystemExit('click rejection anchor missing')
chrome = chrome.replace(click_reject_old, click_reject_new, 1)

monitor_old = '''            Activity?.Invoke(monitorId, "Composer delivery was not confirmed. Exactly-once guard suppressed blind resend; monitoring will reconcile from observed ChatGPT state.");
            return SendWhenReadyOutcome.ReconcileRequired;'''
monitor_new = '''            if (await _chrome.IsComposerDefinitelyStillAwaitingSubmitAsync(tab, message, cancellationToken))
            {
                Activity?.Invoke(monitorId, "Composer still contains the exact pending message with Send enabled. No delivery evidence exists; safe retry remains authorized.");
                return SendWhenReadyOutcome.DeferredBeforePhysicalSubmit;
            }

            Activity?.Invoke(monitorId, "Composer delivery was not confirmed. Exactly-once guard suppressed blind resend; monitoring will reconcile from observed ChatGPT state.");
            return SendWhenReadyOutcome.ReconcileRequired;'''
if monitor_old not in monitor:
    raise SystemExit('monitor delivery classification anchor missing')
monitor = monitor.replace(monitor_old, monitor_new, 1)

chrome_path.write_text(chrome, encoding='utf-8')
monitor_path.write_text(monitor, encoding='utf-8')
readiness_path.write_text(readiness, encoding='utf-8')

version_files = [
    root / 'src/GPTDeskTop/GPTDeskTop.csproj',
    root / 'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
    root / 'src/GPTDeskTop.Setup/Program.cs',
    root / 'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj',
]
for path in version_files:
    text = path.read_text(encoding='utf-8').replace('2.0.12', '2.0.13')
    path.write_text(text, encoding='utf-8')

regression = root / 'tests/GPTDeskTop.RuntimeTests/FreshChatPhysicalSubmitFallbackRegressionTests.cs'
regression.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatPhysicalSubmitFallbackRegressionTests
{
    [Fact]
    public void RejectedVerifiedDeliveryIsReclassifiedWhenExactComposerProvesNoSubmit()
    {
        var source = MonitorSource();
        var method = Slice(source, "private async Task<SendWhenReadyOutcome> SendWhenReadyAsync", "private async Task ApplyModelRouteAsync");
        var accepted = method.IndexOf("if (accepted)", StringComparison.Ordinal);
        var unsentProbe = method.IndexOf("IsComposerDefinitelyStillAwaitingSubmitAsync", StringComparison.Ordinal);
        var reconcile = method.IndexOf("Composer delivery was not confirmed", StringComparison.Ordinal);
        Assert.True(accepted >= 0 && unsentProbe > accepted && reconcile > unsentProbe);
        Assert.Contains("return SendWhenReadyOutcome.DeferredBeforePhysicalSubmit;", method[unsentProbe..reconcile], StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitelyUnsentProbeRequiresStableExactEnabledComposer()
    {
        var source = ChromeSource();
        var method = Slice(source, "public async Task<bool> IsComposerDefinitelyStillAwaitingSubmitAsync", "private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync");
        Assert.Contains("confirmationsRequired = 3", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEditorMatchesExpectedAsync", method, StringComparison.Ordinal);
        Assert.Contains("!readiness.IsGenerating", method, StringComparison.Ordinal);
        Assert.Contains("!readiness.HasRenderedError", method, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent", method, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonEnabled", method, StringComparison.Ordinal);
        Assert.Contains("confirmed-unsent-composer", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedMouseSubmitFallsBackToTrustedNativeEnterAndReobservesReceipt()
    {
        var source = ChromeSource();
        var method = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");
        var rejected = method.IndexOf("ImmediatePhysicalSubmitObservation.ClickNotAccepted", StringComparison.Ordinal);
        var enter = method.IndexOf("TryDispatchNativeEnterSubmitAsync", rejected, StringComparison.Ordinal);
        var observe = method.IndexOf("ObserveImmediatePhysicalSubmitAsync", enter, StringComparison.Ordinal);
        Assert.True(rejected >= 0 && enter > rejected && observe > enter);
        Assert.Contains("native-enter-submit-confirmed", method, StringComparison.Ordinal);
        Assert.Contains("native-enter-submit-ambiguous", method, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEnterUsesRawKeyDownAndKeyUpWithoutCharInsertion()
    {
        var source = ChromeSource();
        var method = Slice(source, "private async Task<bool> TryDispatchNativeEnterSubmitAsync", "public async Task<bool> IsComposerDefinitelyStillAwaitingSubmitAsync");
        Assert.Contains("Input.dispatchKeyEvent", method, StringComparison.Ordinal);
        Assert.Contains("type = \"rawKeyDown\"", method, StringComparison.Ordinal);
        Assert.Contains("type = \"keyUp\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("type = \"char\"", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEditorMatchesExpectedAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SendButtonSelectionNeverStopsAtHiddenStaleTestIdButton()
    {
        var chrome = ChromeSource();
        var readiness = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatComposerReadinessScript.cs")));
        Assert.DoesNotContain("document.querySelector('button[data-testid=\"send-button\"]') ||", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('button[data-testid=\"send-button\"]') ||", readiness, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('data-testid') === 'send-button'", chrome, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('data-testid') === 'send-button'", readiness, StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string MonitorSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
''', encoding='utf-8')

print('v2.0.13 physical submit retry patch applied')
