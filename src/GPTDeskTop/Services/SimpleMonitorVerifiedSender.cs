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
/// This keeps the UI responsive without turning a pre-submit CDP timeout into a false
/// "physical send outcome uncertain" block.
/// </summary>
internal static class SimpleMonitorVerifiedSender
{
    private static readonly MethodInfo EvaluateMethod = typeof(ChromeDevToolsService)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "EvaluateAsync" && method.GetParameters().Length == 4);

    private static readonly TimeSpan PreSubmitRetryWindow = TimeSpan.FromSeconds(15);
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

        var preSubmitDeadline = DateTimeOffset.UtcNow + PreSubmitRetryWindow;
        UserTurnSnapshot before = default;

        while (DateTimeOffset.UtcNow < preSubmitDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // PRE-SUBMIT PHASE. These operations may populate/re-populate the composer, but they
                // never dispatch the send-button click. A CDP timeout here is therefore safe to retry.
                var prepared = await PrepareComposerAsync(chrome, tab, expected, cancellationToken).ConfigureAwait(false);
                if (!prepared)
                {
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var ready = await IsComposerReadyToSubmitAsync(chrome, tab, expected, cancellationToken).ConfigureAwait(false);
                if (!ready)
                {
                    await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Capture the user-turn baseline immediately before dispatching the physical submit.
                // If this passive read times out, retry is still safe because no click was dispatched.
                before = await ReadUserTurnSnapshotAsync(chrome, tab, cancellationToken).ConfigureAwait(false);

                // PHYSICAL-SUBMIT BOUNDARY. From this call onward a transport timeout is uncertain:
                // the JavaScript click may have executed even if the CDP reply was lost.
                var submitted = await DispatchSubmitOnceAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (!submitted)
                {
                    // The submit expression completed and explicitly reported that no click occurred.
                    // This is still pre-submit and can be retried safely in-place.
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
                // A failure before DispatchSubmitOnceAsync is not a physical-send uncertainty.
                // Never refresh/rebind here; simply retry the same prepared text in-place.
                await Task.Delay(PreSubmitPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        // No submit command was dispatched within the pre-submit retry window.
        // Returning false lets the caller handle the condition without claiming an uncertain send.
        return false;
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
  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;

  const current = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
    ? editor.value
    : (editor.innerText || editor.textContent || '');
  if ((current || '').trim() === text.trim()) return true;

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
  }
  const after = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
    ? editor.value
    : (editor.innerText || editor.textContent || '');
  return (after || '').trim() === text.trim();
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
  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
  if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled="true"]')) return false;
  const current = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
    ? editor.value
    : (editor.innerText || editor.textContent || '');
  if ((current || '').trim() !== expected.trim()) return false;
  const sendButton = document.querySelector('button[data-testid="send-button"]') ||
    [...document.querySelectorAll('button')].find(button => {
      if (!visible(button)) return false;
      const label = button.getAttribute('aria-label') || '';
      return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());
    });
  return !!sendButton
    && !sendButton.disabled
    && sendButton.getAttribute('aria-disabled') !== 'true'
    && visible(sendButton);
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
  const stop = document.querySelector('button[data-testid="stop-button"]');
  if (visible(stop)) return false;
  const sendButton = document.querySelector('button[data-testid="send-button"]') ||
    [...document.querySelectorAll('button')].find(button => {
      if (!visible(button)) return false;
      const label = button.getAttribute('aria-label') || '';
      return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());
    });
  if (!sendButton || sendButton.disabled || sendButton.getAttribute('aria-disabled') === 'true' || !visible(sendButton)) return false;
  sendButton.click();
  try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
  return true;
})()
""";

        try
        {
            // The Runtime.evaluate request that contains sendButton.click() is the exact uncertainty
            // boundary. If the response is lost, the click may already have executed.
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
