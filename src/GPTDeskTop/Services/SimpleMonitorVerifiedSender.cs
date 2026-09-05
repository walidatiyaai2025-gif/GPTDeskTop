using System.Reflection;
using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Monitor-only send path with one explicit physical-submit boundary.
/// Composer preparation may retry because it cannot submit. Once the atomic submit command is
/// dispatched, any transport uncertainty fails closed and receipt verification stays read-only.
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
        // Composer preparation is safe to retry because it only writes the draft. After the draft
        // is exact, one atomic Runtime.evaluate validates the current UI, captures the user-turn
        // baseline and performs at most one physical submit. This avoids the former chain of several
        // vulnerable CDP round-trips between "draft ready" and the actual submit.
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

                var attempt = await DispatchPreparedComposerOnceAsync(
                    chrome,
                    tab,
                    expected,
                    cancellationToken).ConfigureAwait(false);

                if (attempt.RateLimited)
                    return false;

                if (!attempt.Submitted)
                {
                    // The atomic command completed and explicitly reported that no click/requestSubmit
                    // occurred. Retrying remains safe because the physical-submit boundary was not crossed.
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return await VerifyReceiptReadOnlyAsync(
                    chrome,
                    tab,
                    expected,
                    attempt.Before,
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
                // Failures here can only come from pre-submit composer preparation. The atomic submit
                // method converts every transport failure after its Runtime.evaluate dispatch into
                // SimpleMonitorSendUncertainException, so this retry can never duplicate a send.
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

    private static async Task<AtomicSubmitResult> DispatchPreparedComposerOnceAsync(
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
  const rateLimitPattern = /too many requests|making requests too quickly|temporarily limited access|temporarily limited access to your conversations|please wait a few minutes before trying again|rate[ -]?limit|http\s*429|error\s*429|status\s*429/i;
  const transcriptSelector = '[data-message-author-role="user"],[data-message-author-role="assistant"]';
  const rateLimitVisible = () => {
    const roots = [
      ...document.querySelectorAll('[role="dialog"], [aria-modal="true"], [role="alert"], [aria-live="assertive"], [data-state="open"], [data-radix-portal]')
    ];
    return roots.some(element => {
      if (!visible(element) || element.closest(transcriptSelector)) return false;
      const text = (element.innerText || element.textContent || '').trim();
      return text.length > 0 && text.length <= 4000 && rateLimitPattern.test(text);
    });
  };
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

  const empty = reason => ({ submitted: false, rateLimited: false, reason, beforeCount: 0, beforeLastText: '', path: '' });
  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return empty('generating');

  const editor = findEditor();
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return empty('editor-not-ready');
  if (normalize(readEditor(editor)) !== normalize(expected)) return empty('draft-mismatch');
  if (rateLimitVisible()) return { ...empty('rate-limited'), rateLimited: true };

  const userTurns = [...document.querySelectorAll('[data-message-author-role="user"]')];
  const beforeCount = userTurns.length;
  const beforeLastText = beforeCount
    ? (userTurns[beforeCount - 1].innerText || userTurns[beforeCount - 1].textContent || '').trim()
    : '';
  const target = findSendButton(editor);

  if (target.button) {
    target.button.click();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return { submitted: true, rateLimited: false, reason: '', beforeCount, beforeLastText, path: 'button' };
  }

  if (target.form && typeof target.form.requestSubmit === 'function') {
    target.form.requestSubmit();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return { submitted: true, rateLimited: false, reason: '', beforeCount, beforeLastText, path: 'form' };
  }

  return { submitted: false, rateLimited: false, reason: 'submit-control-not-ready', beforeCount, beforeLastText, path: '' };
})()
""";

        JsonElement value;
        try
        {
            // ATOMIC PHYSICAL-SUBMIT BOUNDARY. This single Runtime.evaluate performs the final exact
            // draft/safety validation, captures the receipt baseline and performs at most one submit.
            // If its CDP response is lost, the submit may already have happened and retry is forbidden.
            value = await EvaluateAsync(chrome, tab, expression, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SimpleMonitorSendUncertainException(
                $"The atomic submit command was dispatched but its CDP result was not confirmed ({ex.Message}). Automatic retry is blocked.",
                ex);
        }

        if (value.ValueKind != JsonValueKind.Object)
            return new AtomicSubmitResult(false, false, new UserTurnSnapshot(0, string.Empty));

        var submitted = value.TryGetProperty("submitted", out var submittedElement)
            && submittedElement.ValueKind == JsonValueKind.True;
        var rateLimited = value.TryGetProperty("rateLimited", out var rateElement)
            && rateElement.ValueKind == JsonValueKind.True;
        var beforeCount = value.TryGetProperty("beforeCount", out var countElement) && countElement.TryGetInt32(out var count)
            ? count
            : 0;
        var beforeLastText = value.TryGetProperty("beforeLastText", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        return new AtomicSubmitResult(
            submitted,
            rateLimited,
            new UserTurnSnapshot(beforeCount, beforeLastText.Trim()));
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
                // Receipt checks are read-only. A transient Runtime.evaluate timeout after submit
                // never authorizes another physical send; keep observing until the receipt deadline.
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
    private readonly record struct AtomicSubmitResult(bool Submitted, bool RateLimited, UserTurnSnapshot Before);
}

internal sealed class SimpleMonitorSendUncertainException : Exception
{
    internal SimpleMonitorSendUncertainException(string message) : base(message) { }
    internal SimpleMonitorSendUncertainException(string message, Exception innerException) : base(message, innerException) { }
}
