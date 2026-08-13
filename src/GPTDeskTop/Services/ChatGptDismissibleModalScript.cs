namespace GPTDeskTop.Services;

public static class ChatGptDismissibleModalScript
{
    public const string Expression = """
(() => {
  const visible = el => {
    if (!el) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const normalize = v => (v || '').replace(/\s+/g, ' ').trim().toLowerCase();
  const humanMarkers = ['verify you are human','human verification','captcha','security check','checking your browser'];
  const reminderMarkers = ['just checking in',"you've been chatting a while",'is this a good time for a break'];
  const pageText = normalize(document.body?.innerText || '');
  if (humanMarkers.some(m => pageText.includes(m))) return { result: 'HumanVerificationDetected' };
  const dialogs = [...document.querySelectorAll('[role="dialog"],dialog')].filter(visible);
  const dialog = dialogs.find(d => reminderMarkers.some(m => normalize(d.innerText || d.textContent).includes(m)));
  if (!dialog) return { result: 'NotPresent' };
  const buttons = [...dialog.querySelectorAll('button,[role="button"]')].filter(visible);
  const close = buttons.find(b => /close|dismiss|إغلاق|اغلاق|×|✕|✖/i.test(`${b.getAttribute('aria-label') || ''} ${b.getAttribute('title') || ''} ${b.innerText || ''}`)) || buttons.find(b => !normalize(b.innerText || b.textContent));
  if (!close) return { result: 'StillVisible' };
  close.click();
  return { result: 'Dismissed' };
})()
""";
}
