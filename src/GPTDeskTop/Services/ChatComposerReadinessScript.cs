namespace GPTDeskTop.Services;

/// <summary>
/// Read-only DOM probe. It deliberately does not focus the editor, change selection, dispatch
/// input/change events, click controls, or synthesize keyboard input.
/// </summary>
public static class ChatComposerReadinessScript
{
    public const string Expression = """
(() => {
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };

  const composerForm = document.querySelector('form[data-chatgpt-composer]') ||
    document.querySelector('form[data-thread-find-composer="true"]') ||
    null;
  const editor = composerForm?.querySelector(
      '.ProseMirror[contenteditable="true"][role="textbox"],[contenteditable="true"][data-composer-markdown],[contenteditable="true"][role="textbox"],#prompt-textarea,[data-testid="prompt-textarea"],textarea[placeholder]') ||
    document.querySelector('#prompt-textarea') ||
    document.querySelector('[data-testid="prompt-textarea"]') ||
    document.querySelector('textarea[placeholder]');
  const send = composerForm?.querySelector(
      'button[type="submit"][aria-label="Send"],button[type="submit"][aria-label="إرسال"],button[data-testid="send-button"],button[data-testid="composer-submit-button"],button[type="submit"],input[type="submit"]') ||
    document.querySelector('button[data-testid="send-button"]') ||
    document.querySelector('button[data-testid="composer-submit-button"]') ||
    [...document.querySelectorAll('button')].find(button => {
      if (!visible(button)) return false;
      const label = (button.getAttribute('aria-label') || '').trim();
      return /^(send|send message|send prompt|submit prompt|إرسال|إرسال الرسالة|إرسال المطالبة)$/i.test(label);
    });
  const stop = document.querySelector('button[data-testid="stop-button"]');

  const editorDisabled = !editor || editor.matches(':disabled,[aria-disabled="true"]');
  const sendDisabled = !send || send.disabled || send.getAttribute('aria-disabled') === 'true';

  return {
    editorPresent: !!editor && visible(editor),
    editorEnabled: !!editor && visible(editor) && !editorDisabled,
    sendButtonPresent: !!send && visible(send),
    sendButtonEnabled: !!send && visible(send) && !sendDisabled,
    isGenerating: !!stop && visible(stop)
  };
})()
""";
}
