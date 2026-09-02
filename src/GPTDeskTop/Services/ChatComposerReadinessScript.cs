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

  const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
  const send = [...document.querySelectorAll('button')].find(button => {
    if (!visible(button) || button.disabled || button.getAttribute('aria-disabled') === 'true') return false;
    if (button.getAttribute('data-testid') === 'send-button') return true;
    const label = (button.getAttribute('aria-label') || '').trim();
    return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label);
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
