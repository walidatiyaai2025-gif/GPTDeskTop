namespace GPTDeskTop.Services;

public static class ChatComposerHealthScript
{
    public const string Expression = """
(() => {
  const visible = el => {
    if (!el) return false;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const composer = [...document.querySelectorAll('textarea,[contenteditable="true"]')].find(visible);
  return { available: !!composer, disabled: !!composer?.disabled, editable: !!composer && composer.getAttribute('aria-disabled') !== 'true' };
})()
""";
}
