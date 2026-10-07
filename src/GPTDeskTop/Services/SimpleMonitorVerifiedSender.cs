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
  const selectors = [
    '[data-message-author-role="user"]',
    '[data-turn="user"]',
    '[data-author="user"]',
    '[data-author-role="user"]',
    '[data-message-role="user"]',
    '[data-testid*="user-message"]',
    '.user-message-bubble-color',
    '[class*="user-message-bubble"]'
  ];
  const seen = new Set();
  const messages = [];
  for (const selector of selectors) {
    for (const node of document.querySelectorAll(selector)) {
      const turn = node.closest('[data-testid^="conversation-turn-"],article') || node;
      if (seen.has(turn)) continue;
      seen.add(turn);
      messages.push(node);
    }
  }
  const lastNode = messages.length ? messages[messages.length - 1] : null;
  const last = lastNode ? (lastNode.innerText || lastNode.textContent || '').trim() : '';
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
                    attempt.BeforeExactCount,
                    attempt.BeforeAssistantCount,
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
  const semanticUserSelectors = [
    '[data-message-author-role="user"]',
    '[data-turn="user"]',
    '[data-author="user"]',
    '[data-author-role="user"]',
    '[data-message-role="user"]',
    '[data-testid*="user-message"]',
    '.user-message-bubble-color',
    '[class*="user-message-bubble"]'
  ];
  const semanticAssistantSelector = [
    '[data-message-author-role="assistant"]',
    '[data-turn="assistant"]',
    '[data-author="assistant"]',
    '[data-author-role="assistant"]',
    '[data-message-role="assistant"]',
    '[data-testid*="assistant-message"]'
  ].join(',');
  const readSemanticUserTurns = () => {
    const seen = new Set();
    const nodes = [];
    for (const selector of semanticUserSelectors) {
      for (const node of document.querySelectorAll(selector)) {
        const turn = node.closest('[data-testid^="conversation-turn-"],article') || node;
        if (seen.has(turn)) continue;
        seen.add(turn);
        nodes.push(node);
      }
    }
    return nodes;
  };
  const countExactTranscriptText = text => {
    const wanted = normalize(text);
    if (!wanted) return 0;
    const root = document.querySelector('main') || document.querySelector('[role="main"]') || document.body;
    if (!root) return 0;
    const excluded = node => !!node.closest(
      'form[data-chatgpt-composer],form[data-thread-find-composer="true"],nav,aside,header,[contenteditable="true"],[role="textbox"],textarea,input,button');
    return [...root.querySelectorAll('article,[data-testid^="conversation-turn-"],[data-testid*="conversation-turn"] div,[data-testid*="conversation-turn"] p,div,p')]
      .filter(node => {
        if (excluded(node)) return false;
        if (normalize(node.innerText || node.textContent || '') !== wanted) return false;
        return ![...node.children].some(child =>
          !excluded(child) && normalize(child.innerText || child.textContent || '') === wanted);
      }).length;
  };
  const countAssistantTurns = () => document.querySelectorAll(semanticAssistantSelector).length;
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

  const userTurns = readSemanticUserTurns();
  const beforeCount = userTurns.length;
  const beforeLastText = beforeCount
    ? (userTurns[beforeCount - 1].innerText || userTurns[beforeCount - 1].textContent || '').trim()
    : '';
  const beforeExactCount = countExactTranscriptText(expected);
  const beforeAssistantCount = countAssistantTurns();
  const target = findSendButton(editor);

  if (target.button) {
    target.button.click();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return { submitted: true, rateLimited: false, reason: '', beforeCount, beforeLastText, beforeExactCount, beforeAssistantCount, path: 'button' };
  }

  if (target.form && typeof target.form.requestSubmit === 'function') {
    target.form.requestSubmit();
    try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
    return { submitted: true, rateLimited: false, reason: '', beforeCount, beforeLastText, beforeExactCount, beforeAssistantCount, path: 'form' };
  }

  return { submitted: false, rateLimited: false, reason: 'submit-control-not-ready', beforeCount, beforeLastText, beforeExactCount, beforeAssistantCount, path: '' };
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
            return new AtomicSubmitResult(false, false, new UserTurnSnapshot(0, string.Empty), 0, 0);

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
        var beforeExactCount = value.TryGetProperty("beforeExactCount", out var exactElement)
            && exactElement.TryGetInt32(out var exactCount)
            ? exactCount
            : 0;
        var beforeAssistantCount = value.TryGetProperty("beforeAssistantCount", out var assistantElement)
            && assistantElement.TryGetInt32(out var assistantCount)
            ? assistantCount
            : 0;

        return new AtomicSubmitResult(
            submitted,
            rateLimited,
            new UserTurnSnapshot(beforeCount, NormalizeReceiptText(beforeLastText)),
            beforeExactCount,
            beforeAssistantCount);
    }

    private static async Task<bool> VerifyReceiptReadOnlyAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expected,
        UserTurnSnapshot before,
        int beforeExactCount,
        int beforeAssistantCount,
        CancellationToken cancellationToken)
    {
        var normalizedExpected = NormalizeReceiptText(expected);
        var deadline = DateTimeOffset.UtcNow + ReceiptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var evidence = await ReadReceiptEvidenceAsync(chrome, tab, normalizedExpected, cancellationToken)
                    .ConfigureAwait(false);

                // Strongest receipt: the exact visible transcript text appeared after the atomic
                // submit baseline. This survives ChatGPT changing/removing data-message-author-role.
                if (evidence.ExactCount > beforeExactCount)
                    return true;

                if (evidence.User.Count > before.Count)
                {
                    if (string.Equals(
                            NormalizeReceiptText(evidence.User.LastText),
                            normalizedExpected,
                            StringComparison.Ordinal))
                        return true;

                    throw new SimpleMonitorSendUncertainException(
                        "A different user turn appeared after the physical submit. Automatic retry is blocked.");
                }

                // If ChatGPT is already generating or a new assistant turn appeared, the server
                // necessarily accepted the preceding user submit. Treat that as a confirmed receipt
                // rather than blocking merely because the user-bubble DOM changed.
                if (evidence.IsGenerating || evidence.AssistantCount > beforeAssistantCount)
                    return true;
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
            "The physical submit was issued, but neither the exact user-turn receipt nor response evidence was confirmed within 15 seconds. Automatic retry is blocked.");
    }

    private static async Task<ReceiptEvidence> ReadReceiptEvidenceAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken)
    {
        var expectedLiteral = JsonSerializer.Serialize(expected);
        var expression = $$"""
(() => {
  const expected = {{expectedLiteral}};
  const normalize = value => (value || '')
    .replace(/\r\n?/g, '\n')
    .replace(/\u00a0/g, ' ')
    .replace(/[\u200b-\u200d\ufeff]/gi, '')
    .replace(/[ \t]+/g, ' ')
    .trim();
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const userSelectors = [
    '[data-message-author-role="user"]',
    '[data-turn="user"]',
    '[data-author="user"]',
    '[data-author-role="user"]',
    '[data-message-role="user"]',
    '[data-testid*="user-message"]',
    '.user-message-bubble-color',
    '[class*="user-message-bubble"]'
  ];
  const seen = new Set();
  const users = [];
  for (const selector of userSelectors) {
    for (const node of document.querySelectorAll(selector)) {
      const turn = node.closest('[data-testid^="conversation-turn-"],article') || node;
      if (seen.has(turn)) continue;
      seen.add(turn);
      users.push(node);
    }
  }

  const root = document.querySelector('main') || document.querySelector('[role="main"]') || document.body;
  const excluded = node => !!node.closest(
    'form[data-chatgpt-composer],form[data-thread-find-composer="true"],nav,aside,header,[contenteditable="true"],[role="textbox"],textarea,input,button');
  const wanted = normalize(expected);
  const exactCount = root && wanted
    ? [...root.querySelectorAll('article,[data-testid^="conversation-turn-"],[data-testid*="conversation-turn"] div,[data-testid*="conversation-turn"] p,div,p')]
        .filter(node => {
          if (!visible(node) || excluded(node)) return false;
          if (normalize(node.innerText || node.textContent || '') !== wanted) return false;
          return ![...node.children].some(child =>
            !excluded(child) && normalize(child.innerText || child.textContent || '') === wanted);
        }).length
    : 0;

  const assistantSelector = [
    '[data-message-author-role="assistant"]',
    '[data-turn="assistant"]',
    '[data-author="assistant"]',
    '[data-author-role="assistant"]',
    '[data-message-role="assistant"]',
    '[data-testid*="assistant-message"]'
  ].join(',');
  const assistantCount = document.querySelectorAll(assistantSelector).length;
  const stop = document.querySelector('button[data-testid="stop-button"]') ||
    [...document.querySelectorAll('button')].find(button => {
      if (!visible(button)) return false;
      const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.trim();
      return /stop generating|stop responding|إيقاف الإنشاء|إيقاف الرد/i.test(label);
    });
  const lastNode = users.length ? users[users.length - 1] : null;
  return {
    userCount: users.length,
    lastUserText: lastNode ? normalize(lastNode.innerText || lastNode.textContent || '') : '',
    exactCount,
    assistantCount,
    isGenerating: !!stop
  };
})()
""";

        var value = await EvaluateAsync(chrome, tab, expression, cancellationToken).ConfigureAwait(false);
        var userCount = value.TryGetProperty("userCount", out var countElement) && countElement.TryGetInt32(out var count)
            ? count
            : 0;
        var lastUserText = value.TryGetProperty("lastUserText", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        var exactCount = value.TryGetProperty("exactCount", out var exactElement) && exactElement.TryGetInt32(out var exact)
            ? exact
            : 0;
        var assistantCount = value.TryGetProperty("assistantCount", out var assistantElement) && assistantElement.TryGetInt32(out var assistants)
            ? assistants
            : 0;
        var isGenerating = value.TryGetProperty("isGenerating", out var generatingElement)
            && generatingElement.ValueKind == JsonValueKind.True;

        return new ReceiptEvidence(
            new UserTurnSnapshot(userCount, NormalizeReceiptText(lastUserText)),
            exactCount,
            assistantCount,
            isGenerating);
    }

    private static string NormalizeReceiptText(string? value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace('\u00a0', ' ')
            .Replace("\u200b", string.Empty, StringComparison.Ordinal)
            .Replace("\u200c", string.Empty, StringComparison.Ordinal)
            .Replace("\u200d", string.Empty, StringComparison.Ordinal)
            .Replace("\ufeff", string.Empty, StringComparison.Ordinal)
            .Trim();

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
    private readonly record struct ReceiptEvidence(
        UserTurnSnapshot User,
        int ExactCount,
        int AssistantCount,
        bool IsGenerating);
    private readonly record struct AtomicSubmitResult(
        bool Submitted,
        bool RateLimited,
        UserTurnSnapshot Before,
        int BeforeExactCount,
        int BeforeAssistantCount);
}

internal sealed class SimpleMonitorSendUncertainException : Exception
{
    internal SimpleMonitorSendUncertainException(string message) : base(message) { }
    internal SimpleMonitorSendUncertainException(string message, Exception innerException) : base(message, innerException) { }
}
