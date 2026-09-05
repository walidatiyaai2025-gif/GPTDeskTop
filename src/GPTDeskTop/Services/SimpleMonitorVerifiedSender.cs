using System.Reflection;
using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Monitor-only send path with an explicit physical-submit boundary.
///
/// Composer preparation/readiness failures are pre-submit failures: they may be retried in-place
/// because no click command has been dispatched. Once the submit Runtime.evaluate command is
/// dispatched, any transport uncertainty fails closed and receipt verification remains read-only.
/// </summary>
internal static class SimpleMonitorVerifiedSender
{
    private static readonly MethodInfo EvaluateMethod = typeof(ChromeDevToolsService)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "EvaluateAsync" && method.GetParameters().Length == 4);

    private static readonly TimeSpan PreSubmitPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReceiptTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReceiptPollInterval = TimeSpan.FromMilliseconds(250);

    private const string UserTurnSnapshotExpression = """
(() => {
  const messages = [...document.querySelectorAll('[data-message-author-role="user"]')];
  const last = messages.length
    ? (messages[messages.length - 1].innerText || messages[messages.length - 1].textContent || '').trim()
    : '';
  return { count: messages.length, lastText: last };
})()
""";

    private const string RateLimitProbeExpression = """
(() => {
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const pattern = /too many requests|making requests too quickly|temporarily limited access|please wait a few minutes before trying again|http\s*429|error\s*429|status\s*429/i;
  for (const selector of ['[role="dialog"]', '[aria-modal="true"]', '[role="alert"]', '[aria-live="assertive"]']) {
    for (const element of document.querySelectorAll(selector)) {
      if (!visible(element)) continue;
      const text = (element.innerText || element.textContent || '').trim();
      if (text && text.length <= 4000 && pattern.test(text)) return true;
    }
  }
  return false;
})()
""";

    internal static async Task<bool> SendOnceAndVerifyAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(tab);

        var expected = (message ?? string.Empty).Trim();
        if (expected.Length == 0)
            return false;

        // PRE-SUBMIT WAIT CONTRACT:
        // Keep waiting/retrying in-place until the selected same-chat composer is genuinely ready.
        // A prepared draft is not a physical send and must never be converted into a terminal
        // "physical submit unavailable" block merely because ChatGPT took longer than an arbitrary
        // local timeout to expose/enable its send control. Stop/cancellation remains authoritative.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var prepared = await PrepareComposerAsync(chrome, tab, expected, cancellationToken).ConfigureAwait(false);
                if (!prepared)
                {
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // A rate-limit dialog can appear after the outer send gate but before the click.
                // Detect it again here while we are still safely pre-submit and return to the
                // runner's global breaker path without dispatching a physical submit.
                if (await IsRateLimitVisibleAsync(chrome, tab, cancellationToken).ConfigureAwait(false))
                    return false;

                var ready = await IsComposerReadyToSubmitAsync(chrome, tab, expected, cancellationToken).ConfigureAwait(false);
                if (!ready)
                {
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Capture the user-turn baseline immediately before dispatching the physical submit.
                // If this passive read times out, retry is still safe because no click was dispatched.
                var before = await ReadUserTurnSnapshotAsync(chrome, tab, cancellationToken).ConfigureAwait(false);

                if (await IsRateLimitVisibleAsync(chrome, tab, cancellationToken).ConfigureAwait(false))
                    return false;

                // PHYSICAL-SUBMIT BOUNDARY. From this call onward a transport timeout is uncertain:
                // the JavaScript click/requestSubmit may have executed even if the CDP reply was lost.
                var submitted = await DispatchSubmitOnceAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (!submitted)
                {
                    // The submit expression completed and explicitly reported that no click/submit
                    // occurred. This remains pre-submit and is safe to retry indefinitely in-place.
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return await VerifyReceiptReadOnlyAsync(
                    chrome,
                    tab,
                    expected,
                    before,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SimpleMonitorSendUncertainException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Any failure before DispatchSubmitOnceAsync is not a physical-send uncertainty.
                // Keep the same prepared draft and retry in-place; do not refresh/rebind or claim
                // that a user turn may have been submitted.
                await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> PrepareComposerAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken)
    {
        var textLiteral = JsonSerializer.Serialize(expected);
        var expression = $$"""
(() => {
  const text = {{textLiteral}};
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const normalize = value => (value || '')
    .replace(/\r\n?/g, '\n')
    .replace(/\u00a0/g, ' ')
    .replace(/[\u200b-\u200d\ufeff]/gi, '')
    .trim();
  const findEditor = () =>
    document.querySelector('#prompt-textarea') ||
    document.querySelector('textarea[placeholder]') ||
    document.querySelector('[contenteditable="true"][data-lexical-editor="true"]') ||
    [...document.querySelectorAll('[contenteditable="true"][role="textbox"]')].find(visible) ||
    null;
  const readEditor = editor => editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
    ? editor.value
    : (editor.innerText || editor.textContent || '');

  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const editor = findEditor();
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;

  const current = readEditor(editor);
  if (normalize(current) === normalize(text)) return true;

  editor.focus();
  if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) {
    const setter = Object.getOwnPropertyDescriptor(
      editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype,
      'value')?.set;
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
    editor.dispatchEvent(new Event('change', { bubbles: true }));
  }

  return normalize(readEditor(editor)) === normalize(text);
})()
""";

        var value = await EvaluateAsync(chrome, tab, expression, cancellationToken).ConfigureAwait(false);
        return value.ValueKind == JsonValueKind.True;
    }

    private static async Task<bool> IsComposerReadyToSubmitAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken)
    {
        var textLiteral = JsonSerializer.Serialize(expected);
        var expression = $$"""
(() => {
  const expected = {{textLiteral}};
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const normalize = value => (value || '')
    .replace(/\r\n?/g, '\n')
    .replace(/\u00a0/g, ' ')
    .replace(/[\u200b-\u200d\ufeff]/gi, '')
    .trim();
  const findEditor = () =>
    document.querySelector('#prompt-textarea') ||
    document.querySelector('textarea[placeholder]') ||
    document.querySelector('[contenteditable="true"][data-lexical-editor="true"]') ||
    [...document.querySelectorAll('[contenteditable="true"][role="textbox"]')].find(visible) ||
    null;
  const readEditor = editor => editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
    ? editor.value
    : (editor.innerText || editor.textContent || '');
  const enabledCandidate = button => {
    if (!button || !visible(button)) return false;
    if (button.matches(':disabled,[aria-disabled="true"]')) return false;
    const meta = `${button.getAttribute('data-testid') || ''} ${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
    if (/stop|voice|microphone|audio|attach|upload|cancel|إيقاف|صوت|ميكروفون/i.test(meta)) return false;
    return true;
  };
  const findSendButton = editor => {
    const form = editor?.closest('form');
    const composer = form || editor?.closest('[data-testid*="composer"]') || editor?.closest('[class*="composer"]') || editor?.parentElement?.parentElement?.parentElement || document;
    const strict = root => {
      const byTestId = root.querySelector('button[data-testid="send-button"], [role="button"][data-testid="send-button"]');
      if (enabledCandidate(byTestId)) return byTestId;
      const labeled = [...root.querySelectorAll('button,[role="button"]')].find(button => {
        if (!enabledCandidate(button)) return false;
        const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
        return /^(send|send message|send prompt|submit|إرسال|إرسال الرسالة)$/i.test(label);
      });
      if (labeled) return labeled;
      if (root instanceof HTMLFormElement) {
        const submit = [...root.querySelectorAll('button[type="submit"],input[type="submit"]')].find(enabledCandidate);
        if (submit) return submit;
      }
      return null;
    };
    return strict(composer) || (composer !== document ? strict(document) : null);
  };

  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const editor = findEditor();
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;
  if (normalize(readEditor(editor)) !== normalize(expected)) return false;

  if (findSendButton(editor)) return true;

  // Keep the readiness gate aligned with the single-submit dispatch path. ChatGPT may expose
  // the composer action without the historical send-button metadata, while the exact editor form
  // remains a valid submit boundary. DispatchSubmitOnceAsync will invoke requestSubmit() once on
  // this same form; no broad click, Enter-key fallback, reload, or second submit is introduced.
  const form = editor.closest('form');
  return !!(form && typeof form.requestSubmit === 'function');
})()
""";

        var value = await EvaluateAsync(chrome, tab, expression, cancellationToken).ConfigureAwait(false);
        return value.ValueKind == JsonValueKind.True;
    }

    private static async Task<bool> DispatchSubmitOnceAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        const string submitExpression = """
(() => {
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const findEditor = () =>
    document.querySelector('#prompt-textarea') ||
    document.querySelector('textarea[placeholder]') ||
    document.querySelector('[contenteditable="true"][data-lexical-editor="true"]') ||
    [...document.querySelectorAll('[contenteditable="true"][role="textbox"]')].find(visible) ||
    null;
  const enabledCandidate = button => {
    if (!button || !visible(button)) return false;
    if (button.matches(':disabled,[aria-disabled="true"]')) return false;
    const meta = `${button.getAttribute('data-testid') || ''} ${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
    if (/stop|voice|microphone|audio|attach|upload|cancel|إيقاف|صوت|ميكروفون/i.test(meta)) return false;
    return true;
  };
  const findSendButton = editor => {
    const form = editor?.closest('form');
    const composer = form || editor?.closest('[data-testid*="composer"]') || editor?.closest('[class*="composer"]') || editor?.parentElement?.parentElement?.parentElement || document;
    const strict = root => {
      const byTestId = root.querySelector('button[data-testid="send-button"], [role="button"][data-testid="send-button"]');
      if (enabledCandidate(byTestId)) return byTestId;
      const labeled = [...root.querySelectorAll('button,[role="button"]')].find(button => {
        if (!enabledCandidate(button)) return false;
        const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
        return /^(send|send message|send prompt|submit|إرسال|إرسال الرسالة)$/i.test(label);
      });
      if (labeled) return labeled;
      if (root instanceof HTMLFormElement) {
        const submit = [...root.querySelectorAll('button[type="submit"],input[type="submit"]')].find(enabledCandidate);
        if (submit) return submit;
      }
      return null;
    };
    return { button: strict(composer) || (composer !== document ? strict(document) : null), form };
  };

  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const editor = findEditor();
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;
  const target = findSendButton(editor);

  if (target.button) {
    target.button.click();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return true;
  }

  // ChatGPT occasionally renders the blue composer action as a form submit control without
  // the historical data-testid/aria-label. requestSubmit is scoped to the exact editor form and
  // is used only when the form exists and the editor is already prepared/validated by the caller.
  if (target.form && typeof target.form.requestSubmit === 'function') {
    target.form.requestSubmit();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return true;
  }

  return false;
})()
""";

        try
        {
            // The Runtime.evaluate request containing click()/requestSubmit() is the exact uncertainty
            // boundary. If its response is lost, the physical submit may already have executed.
            var value = await EvaluateAsync(chrome, tab, submitExpression, cancellationToken).ConfigureAwait(false);
            return value.ValueKind == JsonValueKind.True;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SimpleMonitorSendUncertainException(
                $"The submit command was dispatched but its CDP result was not confirmed ({ex.Message}). Automatic retry is blocked.",
                ex);
        }
    }

    private static async Task<bool> IsRateLimitVisibleAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(chrome, tab, RateLimitProbeExpression, cancellationToken).ConfigureAwait(false);
        return value.ValueKind == JsonValueKind.True;
    }

    private static async Task<bool> VerifyReceiptReadOnlyAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expected,
        UserTurnSnapshot before,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReceiptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var current = await ReadUserTurnSnapshotAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (current.Count > before.Count)
                {
                    if (string.Equals(current.LastText, expected, StringComparison.Ordinal))
                        return true;

                    throw new SimpleMonitorSendUncertainException(
                        "A different user turn appeared after the physical submit. Automatic retry is blocked.");
                }
            }
            catch (SimpleMonitorSendUncertainException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Receipt checks are read-only. A transient Runtime.evaluate timeout after the click
                // is not a reason to send again; keep observing until the receipt deadline.
            }

            await Task.Delay(ReceiptPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new SimpleMonitorSendUncertainException(
            "The physical submit was issued, but its exact user-turn receipt was not confirmed within 15 seconds. Automatic retry is blocked.");
    }

    private static async Task<UserTurnSnapshot> ReadUserTurnSnapshotAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(chrome, tab, UserTurnSnapshotExpression, cancellationToken).ConfigureAwait(false);
        var count = value.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
        var lastText = value.TryGetProperty("lastText", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
        return new UserTurnSnapshot(count, lastText.Trim());
    }

    private static async Task<JsonElement> EvaluateAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expression,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = (Task<JsonElement>)(EvaluateMethod.Invoke(
                chrome,
                new object[] { tab, expression, cancellationToken, false })
                ?? throw new InvalidOperationException("Runtime.evaluate returned no task."));
            return await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private readonly record struct UserTurnSnapshot(int Count, string LastText);
}

internal sealed class SimpleMonitorSendUncertainException : Exception
{
    internal SimpleMonitorSendUncertainException(string message) : base(message) { }
    internal SimpleMonitorSendUncertainException(string message, Exception innerException) : base(message, innerException) { }
}
