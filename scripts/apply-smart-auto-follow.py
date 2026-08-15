from pathlib import Path

root = Path('.')

def replace_once(path: Path, old: str, new: str):
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected exactly one match, found {count}: {old[:100]!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')

app_config = root / 'src/GPTDeskTop/Configuration/AppConfig.cs'
replace_once(app_config,
'''public sealed class ChromeConfig
{
    public string DebuggingBaseUrl { get; set; } = "http://127.0.0.1:9222";
    public int DebuggingPort { get; set; } = 9222;
    public string StartUrl { get; set; } = "https://chatgpt.com/";
}''',
'''public sealed class ChromeConfig
{
    public string DebuggingBaseUrl { get; set; } = "http://127.0.0.1:9222";
    public int DebuggingPort { get; set; } = 9222;
    public string StartUrl { get; set; } = "https://chatgpt.com/";
    public bool SmartAutoFollowEnabled { get; set; } = true;
    public int SmartAutoFollowThrottleMilliseconds { get; set; } = 400;
    public int SmartAutoFollowNearBottomPixels { get; set; } = 180;
}''')

appsettings = root / 'src/GPTDeskTop/appsettings.json'
replace_once(appsettings,
'''    "DebuggingPort": 9222,
    "StartUrl": "https://chatgpt.com/"''',
'''    "DebuggingPort": 9222,
    "StartUrl": "https://chatgpt.com/",
    "SmartAutoFollowEnabled": true,
    "SmartAutoFollowThrottleMilliseconds": 400,
    "SmartAutoFollowNearBottomPixels": 180''')

chrome = root / 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
text = chrome.read_text(encoding='utf-8')

if 'using System.Globalization;' not in text:
    text = text.replace('using System.Diagnostics;\n', 'using System.Diagnostics;\nusing System.Globalization;\n', 1)

text = text.replace('window.__gptDesktopChatStateCache?.version === 4', 'window.__gptDesktopChatStateCache?.version === 5', 1)
text = text.replace('private const string ChatStateInstallExpression = """', 'private const string ChatStateInstallExpressionTemplate = """', 1)
text = text.replace('  const version = 4;\n', '''  const version = 5;
  const smartFollowEnabled = __SMART_ENABLED__;
  const smartFollowThrottleMs = __SMART_THROTTLE_MS__;
  const smartFollowNearBottomPx = __SMART_NEAR_BOTTOM_PX__;
''', 1)

marker = '''  const state = {
    version,
    dirty: true,
    snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '' },
    observer: null,
    read: null
  };'''

controller = r'''  const createSmartFollowController = () => {
    const controller = {
      enabled: smartFollowEnabled,
      mode: smartFollowEnabled ? 'following' : 'disabled',
      sequence: 0,
      event: smartFollowEnabled ? 'installed' : 'disabled',
      timer: 0,
      lastRunAt: 0,
      lastProgrammaticScrollAt: 0,
      touchY: null,
      container: null,
      rearm: null,
      onMutation: null,
      snapshot: null
    };

    const emit = (mode, event) => {
      if (controller.mode === mode && controller.event === event) return;
      controller.mode = mode;
      controller.event = event;
      controller.sequence++;
    };
    const isScrollable = element => {
      if (!element || element === document.body) return false;
      const style = getComputedStyle(element);
      return /(auto|scroll)/i.test(style.overflowY || '') && element.scrollHeight > element.clientHeight + 8;
    };
    const resolveContainer = () => {
      if (controller.container?.isConnected && isScrollable(controller.container)) return controller.container;
      const messages = document.querySelectorAll('[data-message-author-role]');
      let current = messages.length ? messages[messages.length - 1].parentElement : null;
      for (let depth = 0; current && depth < 14; depth++, current = current.parentElement) {
        if (isScrollable(current)) {
          controller.container = current;
          return current;
        }
      }
      const scrolling = document.scrollingElement || document.documentElement;
      controller.container = scrolling;
      return scrolling;
    };
    const distanceFromBottom = container => Math.max(0, container.scrollHeight - container.scrollTop - container.clientHeight);
    const nearBottom = container => !!container && distanceFromBottom(container) <= smartFollowNearBottomPx;
    const pause = reason => {
      if (!controller.enabled) return;
      emit('paused-by-user', reason);
    };
    const resumeIfNearBottom = reason => {
      const container = resolveContainer();
      if (nearBottom(container)) emit('following', reason);
    };
    const run = force => {
      controller.timer = 0;
      if (!controller.enabled) return;
      const container = resolveContainer();
      if (!container) return;
      if (controller.mode === 'paused-by-user' && !force) {
        resumeIfNearBottom('near-bottom');
        if (controller.mode === 'paused-by-user') return;
      }
      if (!force && !nearBottom(container)) {
        pause('user-away-from-bottom');
        return;
      }
      controller.lastRunAt = Date.now();
      controller.lastProgrammaticScrollAt = controller.lastRunAt;
      try {
        if (typeof container.scrollTo === 'function') container.scrollTo({ top: container.scrollHeight, behavior: 'auto' });
        else container.scrollTop = container.scrollHeight;
        emit('following', force ? 'rearmed-and-followed' : 'followed-latest');
      } catch {
        emit('following', 'scroll-failed');
      }
    };
    const schedule = force => {
      if (!controller.enabled || controller.timer) return;
      const elapsed = Date.now() - controller.lastRunAt;
      const delay = Math.max(0, smartFollowThrottleMs - elapsed);
      controller.timer = setTimeout(() => run(force), delay);
    };

    controller.rearm = reason => {
      if (!controller.enabled) return;
      emit('following', reason || 'rearmed');
      schedule(true);
    };
    controller.onMutation = () => {
      if (controller.mode === 'following') schedule(false);
    };
    controller.snapshot = () => ({
      mode: controller.mode,
      sequence: controller.sequence,
      event: controller.event
    });

    if (controller.enabled) {
      document.addEventListener('wheel', event => {
        if (event.deltaY < 0) pause('wheel-up');
        else setTimeout(() => resumeIfNearBottom('wheel-near-bottom'), 0);
      }, { capture: true, passive: true });
      document.addEventListener('keydown', event => {
        if (['ArrowUp', 'PageUp', 'Home'].includes(event.key)) pause('keyboard-up');
        else if (event.key === 'End') controller.rearm('keyboard-end');
      }, true);
      document.addEventListener('touchstart', event => {
        controller.touchY = event.touches?.[0]?.clientY ?? null;
      }, { capture: true, passive: true });
      document.addEventListener('touchmove', event => {
        const y = event.touches?.[0]?.clientY;
        if (controller.touchY !== null && typeof y === 'number' && y > controller.touchY + 8) pause('touch-scroll-up');
        controller.touchY = typeof y === 'number' ? y : controller.touchY;
      }, { capture: true, passive: true });
      document.addEventListener('scroll', event => {
        if (Date.now() - controller.lastProgrammaticScrollAt < 180) return;
        const container = resolveContainer();
        if (event.target !== container && event.target !== document) return;
        if (nearBottom(container)) emit('following', 'manual-near-bottom');
        else pause('manual-scroll');
      }, true);
    }
    return controller;
  };

  const state = {
    version,
    dirty: true,
    snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '', autoFollow: { mode: smartFollowEnabled ? 'following' : 'disabled', sequence: 0, event: smartFollowEnabled ? 'installed' : 'disabled' } },
    observer: null,
    autoFollow: null,
    read: null
  };
  state.autoFollow = createSmartFollowController();'''

if marker not in text:
    raise SystemExit('ChromeDevToolsService.cs: state marker not found')
text = text.replace(marker, controller, 1)

old_snapshot = '''    state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText };
    return state.snapshot;'''
new_snapshot = '''    state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText, autoFollow: state.autoFollow?.snapshot?.() || { mode: 'disabled', sequence: 0, event: 'disabled' } };
    if (isGenerating) state.autoFollow?.onMutation?.();
    return state.snapshot;'''
if old_snapshot not in text:
    raise SystemExit('ChromeDevToolsService.cs: snapshot marker not found')
text = text.replace(old_snapshot, new_snapshot, 1)

old_observer = "  state.observer = new MutationObserver(() => { state.dirty = true; });"
new_observer = "  state.observer = new MutationObserver(() => { state.dirty = true; state.autoFollow?.onMutation?.(); });"
if old_observer not in text:
    raise SystemExit('ChromeDevToolsService.cs: observer marker not found')
text = text.replace(old_observer, new_observer, 1)

old_install = '''        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, ChatStateInstallExpression, cancellationToken, false);

        return new ChatPageState('''
new_install = '''        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, BuildChatStateInstallExpression(), cancellationToken, false);

        RecordAutoFollowState(tab, value);
        return new ChatPageState('''
if old_install not in text:
    raise SystemExit('ChromeDevToolsService.cs: read/install marker not found')
text = text.replace(old_install, new_install, 1)

method_anchor = '''    private async Task<ChatPageState> ReadChatStateCoreAsync(ChromeTab tab, CancellationToken cancellationToken)
    {'''
methods = '''    private string BuildChatStateInstallExpression()
    {
        return ChatStateInstallExpressionTemplate
            .Replace("__SMART_ENABLED__", _config.SmartAutoFollowEnabled ? "true" : "false", StringComparison.Ordinal)
            .Replace("__SMART_THROTTLE_MS__", Math.Clamp(_config.SmartAutoFollowThrottleMilliseconds, 150, 2000).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__SMART_NEAR_BOTTOM_PX__", Math.Clamp(_config.SmartAutoFollowNearBottomPixels, 64, 600).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private void RecordAutoFollowState(ChromeTab tab, JsonElement value)
    {
        if (!value.TryGetProperty("autoFollow", out var autoFollow) || autoFollow.ValueKind != JsonValueKind.Object)
            return;
        var sequence = autoFollow.TryGetProperty("sequence", out var sequenceElement) && sequenceElement.TryGetInt64(out var parsedSequence) ? parsedSequence : 0;
        var mode = autoFollow.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() ?? "unknown" : "unknown";
        var eventName = autoFollow.TryGetProperty("event", out var eventElement) ? eventElement.GetString() ?? "state" : "state";
        var shouldRecord = false;
        lock (_autoFollowSync)
        {
            if (!_autoFollowSequences.TryGetValue(tab.Id, out var previousSequence) || previousSequence != sequence)
            {
                _autoFollowSequences[tab.Id] = sequence;
                shouldRecord = true;
            }
        }
        if (shouldRecord)
            RuntimeFlightRecorder.Record("AutoFollow", "StateChanged", mode, eventName);
    }

'''+method_anchor
if method_anchor not in text:
    raise SystemExit('ChromeDevToolsService.cs: method anchor not found')
text = text.replace(method_anchor, methods, 1)

field_anchor = '''    private readonly object _chatStateFailureSync = new();
    private readonly Dictionary<string, int> _chatStateTransportFailures = new(StringComparer.Ordinal);'''
field_new = '''    private readonly object _chatStateFailureSync = new();
    private readonly Dictionary<string, int> _chatStateTransportFailures = new(StringComparer.Ordinal);
    private readonly object _autoFollowSync = new();
    private readonly Dictionary<string, long> _autoFollowSequences = new(StringComparer.Ordinal);'''
if field_anchor not in text:
    raise SystemExit('ChromeDevToolsService.cs: field anchor not found')
text = text.replace(field_anchor, field_new, 1)

close_old = '''finally { _sessionPool.Invalidate(tab.Id); }'''
close_new = '''finally { _sessionPool.Invalidate(tab.Id); lock (_autoFollowSync) _autoFollowSequences.Remove(tab.Id); }'''
if close_old not in text:
    raise SystemExit('ChromeDevToolsService.cs: CloseTab finally marker not found')
text = text.replace(close_old, close_new, 1)

submit_old = '''          sendButton.click();
          return true;'''
submit_new = '''          sendButton.click();
          try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch { }
          return true;'''
if submit_old not in text:
    raise SystemExit('ChromeDevToolsService.cs: send click marker not found')
text = text.replace(submit_old, submit_new, 1)

chrome.write_text(text, encoding='utf-8')

# Add regression coverage. These are intentionally source-contract tests because the controller
# executes inside the live ChatGPT DOM and must not be coupled to a synthetic browser fixture.
test = root / 'tests/GPTDeskTop.RuntimeTests/SmartChatAutoFollowTests.cs'
test.write_text(r'''using GPTDeskTop.Configuration;

namespace GPTDeskTop.RuntimeTests;

public sealed class SmartChatAutoFollowTests
{
    private static string RepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SmartAutoFollowDefaultsAreSafeAndEnabled()
    {
        var config = new ChromeConfig();
        Assert.True(config.SmartAutoFollowEnabled);
        Assert.InRange(config.SmartAutoFollowThrottleMilliseconds, 150, 2000);
        Assert.InRange(config.SmartAutoFollowNearBottomPixels, 64, 600);
    }

    [Fact]
    public void ChatStateCacheContainsSmartFollowPauseResumeAndThrottleContract()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("const version = 5;", source, StringComparison.Ordinal);
        Assert.Contains("createSmartFollowController", source, StringComparison.Ordinal);
        Assert.Contains("paused-by-user", source, StringComparison.Ordinal);
        Assert.Contains("user-away-from-bottom", source, StringComparison.Ordinal);
        Assert.Contains("manual-near-bottom", source, StringComparison.Ordinal);
        Assert.Contains("smartFollowThrottleMs", source, StringComparison.Ordinal);
        Assert.Contains("smartFollowNearBottomPx", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('wheel'", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('touchmove'", source, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('keydown'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationSendRearmsFollowWithoutChangingDeliveryReceiptLogic()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var click = source.IndexOf("sendButton.click();", StringComparison.Ordinal);
        var rearm = source.IndexOf("autoFollow?.rearm?.('automation-send')", click, StringComparison.Ordinal);
        var submitted = source.IndexOf("var submitted = await EvaluateAsync", rearm, StringComparison.Ordinal);
        Assert.True(click >= 0);
        Assert.True(rearm > click);
        Assert.True(submitted > rearm);
        Assert.Contains("VerifiedSendDiagnostics.Record", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFollowIsUiOnlyAndPrivacySafe()
    {
        var source = RepoFile("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("RuntimeFlightRecorder.Record(\"AutoFollow\", \"StateChanged\", mode, eventName)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"AutoFollow\"", source.Replace("RuntimeFlightRecorder.Record(\"AutoFollow\", \"StateChanged\", mode, eventName)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("document.body.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document.cookie", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagedSettingsEnableSmartFollowByDefault()
    {
        var source = RepoFile("src", "GPTDeskTop", "appsettings.json");
        Assert.Contains("\"SmartAutoFollowEnabled\": true", source, StringComparison.Ordinal);
        Assert.Contains("\"SmartAutoFollowThrottleMilliseconds\": 400", source, StringComparison.Ordinal);
        Assert.Contains("\"SmartAutoFollowNearBottomPixels\": 180", source, StringComparison.Ordinal);
    }
}
''', encoding='utf-8')

print('Smart auto-follow patch applied.')
