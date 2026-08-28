from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one source match, found {count}")
    return text.replace(old, new, 1)


chrome_path = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
chrome = chrome_path.read_text(encoding="utf-8")

chrome = replace_once(
    chrome,
    'private const string ChatStateReadExpression = "window.__gptDesktopChatStateCache?.version === 6 ? window.__gptDesktopChatStateCache.read() : null";',
    'private const string ChatStateReadExpression = "window.__gptDesktopChatStateCache?.version === 7 ? window.__gptDesktopChatStateCache.read() : null";',
    "chat-state cache read version",
)
chrome = replace_once(chrome, "  const version = 6;", "  const version = 7;", "chat-state cache install version")

if "const isCurrentTurnElement = element =>" not in chrome:
    pattern = re.compile(
        r"  const findErrorText = \(\) => \{\n.*?\n  \};\n\n  const createSmartFollowController",
        re.S,
    )
    replacement = r'''  const isAfterOrInside = (element, anchor) => {
    if (!element || !anchor) return true;
    if (element === anchor || anchor.contains(element)) return true;
    return !!(anchor.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING);
  };
  const isCurrentTurnElement = element => {
    if (!element) return false;
    const users = document.querySelectorAll('[data-message-author-role="user"]');
    const assistants = document.querySelectorAll('[data-message-author-role="assistant"]');
    const lastUser = users.length ? users[users.length - 1] : null;
    const lastAssistant = assistants.length ? assistants[assistants.length - 1] : null;

    // Historical error/retry cards can remain rendered in long conversations. Recovery authority
    // belongs only to UI that is part of the latest user/assistant turn, never an older DOM card.
    if (lastUser && !isAfterOrInside(element, lastUser)) return false;
    const latestAssistantBelongsToTurn = !!(lastUser && lastAssistant && isAfterOrInside(lastAssistant, lastUser));
    if (latestAssistantBelongsToTurn && !isAfterOrInside(element, lastAssistant)) return false;
    return true;
  };
  const findErrorText = () => {
    const selectors = ['[role="alert"]', '[aria-live="assertive"]', '[data-testid*="error"]', '[data-testid*="retry"]'];
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (!visible(element) || !isCurrentTurnElement(element)) continue;
        const text = (element.innerText || element.textContent || '').trim();
        if (text && errorPattern.test(text)) return text;
      }
    }

    // ChatGPT sometimes renders the delivery-timeout card without an alert/testid on its
    // outer container. Inspect only a small ancestor chain around a visible native Retry
    // control, and only when that control belongs to the latest conversation turn.
    for (const button of document.querySelectorAll('button,[role="button"]')) {
      if (!visible(button) || !isCurrentTurnElement(button)) continue;
      const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''} ${button.innerText || button.textContent || ''}`.trim();
      if (!/\bretry\b|try again|إعادة المحاولة|حاول مرة أخرى/i.test(label)) continue;
      let container = button;
      for (let depth = 0; container && depth < 5; depth++, container = container.parentElement) {
        if (!isCurrentTurnElement(container)) continue;
        const text = (container.innerText || container.textContent || '').trim();
        if (!text || text.length > 600) continue;
        if (errorPattern.test(text)) return text;
      }
    }
    return '';
  };

  const createSmartFollowController'''
    chrome, count = pattern.subn(replacement, chrome, count=1)
    if count != 1:
        raise RuntimeError(f"current-turn error detector: expected one findErrorText block, found {count}")

chrome = replace_once(
    chrome,
    "    const errorText = findErrorText();",
    "    const errorText = isGenerating ? '' : findErrorText();",
    "generation-authoritative error suppression",
)

if "version === 7" not in chrome or "const version = 7;" not in chrome:
    raise RuntimeError("chat-state cache version was not advanced to 7")
if "const isCurrentTurnElement = element =>" not in chrome:
    raise RuntimeError("current-turn error scoping was not installed")
if "const errorText = isGenerating ? '' : findErrorText();" not in chrome:
    raise RuntimeError("generation-authoritative error suppression was not installed")
chrome_path.write_text(chrome, encoding="utf-8")


monitor_path = ROOT / "src/GPTDeskTop/Services/ChatGptMonitorService.cs"
monitor = monitor_path.read_text(encoding="utf-8")
monitor = replace_once(
    monitor,
    "                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText);",
    "                    var isError = !state.IsGenerating && !string.IsNullOrWhiteSpace(state.ErrorText);",
    "monitor generation recovery gate",
)
monitor = replace_once(
    monitor,
    "    private static string GetEffectiveResponse(ChatPageState state) => !string.IsNullOrWhiteSpace(state.ErrorText) ? state.ErrorText.Trim() : state.LastAssistantText.Trim();",
    "    private static string GetEffectiveResponse(ChatPageState state) => state.IsGenerating ? string.Empty : !string.IsNullOrWhiteSpace(state.ErrorText) ? state.ErrorText.Trim() : state.LastAssistantText.Trim();",
    "effective response generation gate",
)
if "var isError = !state.IsGenerating &&" not in monitor:
    raise RuntimeError("monitor generation recovery gate was not installed")
monitor_path.write_text(monitor, encoding="utf-8")


probe_path = ROOT / "src/GPTDeskTop/Services/NoResponseWatchdogProcessProbe.cs"
probe = probe_path.read_text(encoding="utf-8")
if "historical-timeout-card" not in probe:
    slow_pattern = re.compile(
        r'    private static string SlowPage\(\) => """\n.*?\n""";\n\n    private static string ErrorPage',
        re.S,
    )
    slow_replacement = r'''    private static string SlowPage() => """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>QA slow thinking monitor</title></head>
<body>
  <div id="historical-timeout-card" role="alert">Message delivery timed out</div>
  <button aria-label="Retry">Retry</button>
  <div data-message-author-role="user">Current request sent after an older timeout card.</div>
  <main data-message-author-role="assistant"></main>
  <button data-testid="stop-button" aria-label="Stop generating" style="width:140px;height:32px">Stop</button>
  <script>
    const key = 'gptdesktop-passive-wait-slow-load-count';
    const count = Number(sessionStorage.getItem(key) || '0') + 1;
    sessionStorage.setItem(key, String(count));
    const target = document.querySelector('[data-message-author-role="assistant"]');
    target.textContent = `slow-thinking-load-${count}`;
    setTimeout(() => {
      document.querySelector('[data-testid="stop-button"]')?.remove();
      target.textContent = `slow-complete-load-${count}`;
    }, 40000);
  </script>
</body>
</html>
""";

    private static string ErrorPage'''
    probe, count = slow_pattern.subn(slow_replacement, probe, count=1)
    if count != 1:
        raise RuntimeError(f"passive-wait regression fixture: expected one SlowPage block, found {count}")

if "historical-timeout-card" not in probe:
    raise RuntimeError("historical timeout regression fixture was not installed")
probe_path.write_text(probe, encoding="utf-8")

print("False-recovery current-turn fix applied and regression fixture installed.")
