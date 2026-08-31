using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChromeDevToolsService
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int MonitorRecoveryFailureThreshold = 4;
    private const int MonitorRecoveryEndpointGraceAttempts = 8;
    private const int MonitorRecoveryEndpointGraceDelayMs = 250;
    private const string BrowserSessionId = "__gptdesktop_monitor_browser__";
    private const string ChatStateReadExpression = "window.__gptDesktopChatStateCache?.version === 8 ? window.__gptDesktopChatStateCache.read() : null";
    private const string ChatStateInstallExpressionTemplate = """
(() => {
  const key = '__gptDesktopChatStateCache';
  const version = 8;
  const smartFollowEnabled = __SMART_ENABLED__;
  const smartFollowThrottleMs = __SMART_THROTTLE_MS__;
  const smartFollowNearBottomPx = __SMART_NEAR_BOTTOM_PX__;
  const previous = window[key];
  if (previous?.version === version && typeof previous.read === 'function') return previous.read();
  try { previous?.observer?.disconnect?.(); } catch { }

  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const errorPattern = /message delivery timed out|something went wrong|there was an error|network error|failed to (generate|load)|unable to (generate|load)|error generating|حدث خطأ|خطأ في الشبكة|تعذر/i;
  const globalRateLimitPattern = /too many requests|making requests too quickly|temporarily limited access(?: to your conversations)?|please wait a few minutes before trying again/i;
  const findGlobalRateLimitText = () => {
    const selectors = ['[role="dialog"]', '[aria-modal="true"]', '[role="alert"]'];
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (!visible(element)) continue;
        if (element.closest('[data-message-author-role]')) continue;
        const text = (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim();
        if (text && text.length <= 2500 && globalRateLimitPattern.test(text)) return text;
      }
    }
    return '';
  };
  const findStopButton = () => {
    const testStopButton = document.querySelector('button[data-testid="stop-button"]');
    if (visible(testStopButton)) return testStopButton;
    for (const button of document.querySelectorAll('button')) {
      if (!visible(button)) continue;
      const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`;
      if (/stop generating|stop responding|إيقاف الإنشاء|إيقاف الرد/i.test(label)) return button;
    }
    return null;
  };
  const hasStreamingSignal = lastAssistant => {
    if (!lastAssistant) return false;
    const explicitStreamingSelector = '[data-is-streaming="true"],[data-streaming="true"],.result-streaming';
    if (lastAssistant.matches(explicitStreamingSelector) && visible(lastAssistant)) return true;
    for (const element of lastAssistant.querySelectorAll(explicitStreamingSelector)) {
      if (visible(element)) return true;
    }
    return false;
  };
  const isAfterOrInside = (element, anchor) => {
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
      if (!/retry|try again|إعادة المحاولة|حاول مرة أخرى/i.test(label)) continue;
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

  const createSmartFollowController = () => {
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
      snapshot: null,
      markDirty: null
    };

    const emit = (mode, event) => {
      if (controller.mode === mode && controller.event === event) return;
      controller.mode = mode;
      controller.event = event;
      controller.sequence++;
      controller.markDirty?.();
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
    snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '', globalRateLimitText: '', autoFollow: { mode: smartFollowEnabled ? 'following' : 'disabled', sequence: 0, event: smartFollowEnabled ? 'installed' : 'disabled' } },
    observer: null,
    autoFollow: null,
    read: null
  };
  state.autoFollow = createSmartFollowController();
  state.autoFollow.markDirty = () => { state.dirty = true; };
  state.read = () => {
    if (!state.dirty) return state.snapshot;
    state.dirty = false;
    const globalRateLimitText = findGlobalRateLimitText();
    const messages = document.querySelectorAll('[data-message-author-role="assistant"]');
    const lastAssistant = messages.length ? messages[messages.length - 1] : null;
    const stopButton = findStopButton();
    // A visible Stop control is the authoritative generation signal. Streaming CSS/data
    // markers can survive hydration/reconciliation after the response has actually completed.
    const isGenerating = !!stopButton;
    const errorText = isGenerating ? '' : findErrorText();
    const last = !isGenerating && lastAssistant ? (lastAssistant.innerText || '').trim() : '';
    state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText, globalRateLimitText, autoFollow: state.autoFollow?.snapshot?.() || { mode: 'disabled', sequence: 0, event: 'disabled' } };
    if (isGenerating) state.autoFollow?.onMutation?.();
    return state.snapshot;
  };
  state.observer = new MutationObserver(() => { state.dirty = true; state.autoFollow?.onMutation?.(); });
  const root = document.documentElement || document.body;
  if (root) {
    state.observer.observe(root, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: ['aria-busy', 'aria-label', 'title', 'disabled', 'data-is-streaming', 'data-streaming', 'data-testid', 'role', 'class', 'style', 'hidden', 'aria-hidden']
    });
  }
  window[key] = state;
  return state.read();
})()
""";
    private readonly HttpClient _httpClient;
    private readonly ChromeConfig _config;
    private readonly ChromeDevToolsSessionPool _sessionPool = new();
    private readonly SemaphoreSlim _monitorBrowserRecoveryGate = new(1, 1);
    private readonly object _chatStateFailureSync = new();
    private readonly Dictionary<string, int> _chatStateTransportFailures = new(StringComparer.Ordinal);
    private readonly object _autoFollowSync = new();
    private readonly Dictionary<string, long> _autoFollowSequences = new(StringComparer.Ordinal);
    private Process? _monitorChromeProcess;
    private IntPtr _lastKnownWindowHandle = IntPtr.Zero;
    private bool _monitorChromeHidden;
    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config) { _httpClient = httpClient; _config = config; }
    public async Task<List<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken = default) { using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/list", cancellationToken); response.EnsureSuccessStatusCode(); var json = await response.Content.ReadAsStringAsync(cancellationToken); using var document = JsonDocument.Parse(json); var tabs = new List<ChromeTab>(); foreach (var item in document.RootElement.EnumerateArray()) { var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty; if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue; tabs.Add(new ChromeTab { Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty, Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty, Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty, Type = type, WebSocketDebuggerUrl = item.TryGetProperty("webSocketDebuggerUrl", out var ws) ? ws.GetString() ?? string.Empty : string.Empty }); } _sessionPool.Prune(tabs); return tabs.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList(); }
    public Process LaunchMonitorChrome(string? startUrl = null)
    {
        if (IsProcessRunning(_monitorChromeProcess)) return _monitorChromeProcess!;
        try { _monitorChromeProcess?.Dispose(); } catch { }
        _monitorChromeProcess = null;

        var chromePath = FindChromePath();
        var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "ChromeProfile");
        Directory.CreateDirectory(profilePath);
        var url = string.IsNullOrWhiteSpace(startUrl) ? _config.StartUrl : startUrl;
        var arguments = $"--remote-debugging-port={_config.DebuggingPort} --user-data-dir=\"{profilePath}\" --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-features=CalculateNativeWinOcclusion --new-window \"{url}\"";
        _monitorChromeProcess = Process.Start(new ProcessStartInfo { FileName = chromePath, Arguments = arguments, UseShellExecute = true }) ?? throw new InvalidOperationException("Chrome could not be started.");
        return _monitorChromeProcess;
    }
    public async Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken = default) { var existing = await GetTabsAsync(cancellationToken); var controlTab = existing.FirstOrDefault() ?? throw new InvalidOperationException("Monitor Chrome has no controllable page."); var result = await SendCommandAsync(controlTab, "Target.createTarget", new { url }, cancellationToken); var targetId = result.TryGetProperty("targetId", out var id) ? id.GetString() : null; if (string.IsNullOrWhiteSpace(targetId)) throw new InvalidOperationException("Chrome did not return a target ID for the new tab."); for (var attempt = 0; attempt < 40; attempt++) { cancellationToken.ThrowIfCancellationRequested(); await Task.Delay(250, cancellationToken); var tabs = await GetTabsAsync(cancellationToken); var created = tabs.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.Ordinal)); if (created is not null) return created; } throw new TimeoutException("The new Chrome tab did not become ready in time."); }
    public Task<ChromeTab> CreateNewChatTabAsync(CancellationToken cancellationToken = default) => CreateTabAsync(_config.StartUrl, cancellationToken);
    public async Task<bool> CloseTabAsync(ChromeTab tab, CancellationToken cancellationToken = default) { try { using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/close/{Uri.EscapeDataString(tab.Id)}", cancellationToken); return response.IsSuccessStatusCode; } catch { return false; } finally { _sessionPool.Invalidate(tab.Id); lock (_autoFollowSync) _autoFollowSequences.Remove(tab.Id); } }
    public async Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken = default)
    {
        var trackedProcess = _monitorChromeProcess;
        var browserCloseRequested = false;

        try
        {
            var browserTarget = await TryGetBrowserTargetAsync(cancellationToken);
            if (browserTarget is not null)
            {
                try
                {
                    await SendCommandAsync(browserTarget, "Browser.close", new { }, cancellationToken);
                    browserCloseRequested = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (!IsExpectedBrowserCloseDisconnect(ex))
                        ExceptionLogService.Log(ex, "ChromeDevToolsService.CloseMonitorBrowser");
                    browserCloseRequested = true;
                }
                finally
                {
                    _sessionPool.Invalidate(BrowserSessionId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "ChromeDevToolsService.ResolveMonitorBrowser");
        }

        if (!browserCloseRequested)
        {
            List<ChromeTab> tabs;
            try { tabs = await GetTabsAsync(cancellationToken); }
            catch { tabs = []; }

            foreach (var tab in tabs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CloseTabAsync(tab, cancellationToken);
            }
        }

        if (trackedProcess is not null)
        {
            try
            {
                if (!trackedProcess.HasExited && browserCloseRequested)
                {
                    try
                    {
                        await trackedProcess.WaitForExitAsync(cancellationToken)
                            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (!trackedProcess.HasExited)
                {
                    trackedProcess.Kill(entireProcessTree: true);
                    try
                    {
                        await trackedProcess.WaitForExitAsync(cancellationToken)
                            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "ChromeDevToolsService.KillOwnedMonitorBrowser");
            }
            finally
            {
                try { trackedProcess.Dispose(); } catch { }
            }
        }

        _monitorChromeProcess = null;
        _lastKnownWindowHandle = IntPtr.Zero;
        _sessionPool.Clear();
    }
    public async Task<bool> TrySelectModelAsync(ChromeTab tab, string modelLabel, CancellationToken cancellationToken = default) { if (string.IsNullOrWhiteSpace(modelLabel) || string.Equals(modelLabel.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)) return true; var labelLiteral = JsonSerializer.Serialize(modelLabel.Trim()); var expression = $$""" (() => { const requested = {{labelLiteral}}.trim().toLowerCase(); const normalize = value => (value || '').replace(/\s+/g, ' ').trim().toLowerCase(); const visible = element => { const r = element.getBoundingClientRect(); const s = getComputedStyle(element); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; }; const elements = [...document.querySelectorAll('button,[role="button"],[role="menuitem"],[role="option"]')]; const modelButton = elements.find(e => { if (!visible(e)) return false; const label = normalize(e.getAttribute('aria-label')); const text = normalize(e.innerText || e.textContent); return /model|م\u0648\u062f\u064a\u0644|reasoning|thinking|instant/i.test(label + ' ' + text); }); if (modelButton) modelButton.click(); const items = [...document.querySelectorAll('[role="menuitem"],[role="option"],button')]; const target = items.find(e => visible(e) && normalize(e.innerText || e.textContent).includes(requested)); if (!target) return false; target.click(); return true; })() """; try { var result = await EvaluateAsync(tab, expression, cancellationToken, false); return result.ValueKind == JsonValueKind.True; } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, "ChromeDevToolsService.TrySelectModel", null, tab.Id, tab.Title); return false; } }
    public async Task<bool> HideMonitorChromeAsync(CancellationToken cancellationToken = default)
    {
        var handle = await ResolveMonitorWindowHandleAsync(cancellationToken);
        if (handle != IntPtr.Zero)
        {
            _lastKnownWindowHandle = handle;
            ShowWindow(handle, SwHide);
            _monitorChromeHidden = true;
            return true;
        }

        var changed = await SetAllBrowserWindowsStateAsync("minimized", cancellationToken);
        if (changed) _monitorChromeHidden = true;
        return changed;
    }
    public async Task<bool> ShowMonitorChromeAsync(CancellationToken cancellationToken = default)
    {
        var handle = _lastKnownWindowHandle;
        if (handle == IntPtr.Zero || !IsWindow(handle))
            handle = await ResolveMonitorWindowHandleAsync(cancellationToken);

        if (handle != IntPtr.Zero)
        {
            _lastKnownWindowHandle = handle;
            ShowWindow(handle, SwShow);
            ShowWindow(handle, SwRestore);
            _monitorChromeHidden = false;
            return true;
        }

        var changed = await SetAllBrowserWindowsStateAsync("normal", cancellationToken);
        if (changed) _monitorChromeHidden = false;
        return changed;
    }
    private async Task<IntPtr> ResolveMonitorWindowHandleAsync(CancellationToken cancellationToken) { if (_monitorChromeProcess is not null) { for (var attempt = 0; attempt < 20; attempt++) { cancellationToken.ThrowIfCancellationRequested(); try { if (_monitorChromeProcess.HasExited) break; _monitorChromeProcess.Refresh(); if (_monitorChromeProcess.MainWindowHandle != IntPtr.Zero) return _monitorChromeProcess.MainWindowHandle; } catch { break; } await Task.Delay(100, cancellationToken); } } return _lastKnownWindowHandle != IntPtr.Zero && IsWindow(_lastKnownWindowHandle) ? _lastKnownWindowHandle : IntPtr.Zero; }
    private async Task<bool> SetAllBrowserWindowsStateAsync(string state, CancellationToken cancellationToken) { List<ChromeTab> tabs; try { tabs = await GetTabsAsync(cancellationToken); } catch { return false; } if (tabs.Count == 0) return false; var windowIds = new HashSet<int>(); foreach (var tab in tabs) { try { var windowResult = await SendCommandAsync(tab, "Browser.getWindowForTarget", new { targetId = tab.Id }, cancellationToken); if (windowResult.TryGetProperty("windowId", out var windowIdElement)) windowIds.Add(windowIdElement.GetInt32()); } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, "ChromeDevToolsService.GetWindowForTarget", null, tab.Id, tab.Title); } } var changed = false; foreach (var windowId in windowIds) { try { await SendCommandAsync(tabs[0], "Browser.setWindowBounds", new { windowId, bounds = new { windowState = state } }, cancellationToken); changed = true; } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, $"ChromeDevToolsService.SetWindowState({state})"); } } return changed; }
    public async Task<ChatPageState> GetChatStateAsync(ChromeTab tab, CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await ReadChatStateCoreAsync(tab, cancellationToken);
            ResetChatStateTransportFailures(tab);
            return state;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
        {
            var failureCount = IncrementChatStateTransportFailures(tab);
            if (failureCount < MonitorRecoveryFailureThreshold)
                throw;

            if (!await RecoverMonitorTabAsync(tab, cancellationToken))
                throw;

            ResetChatStateTransportFailures(tab);
            return await ReadChatStateCoreAsync(tab, cancellationToken);
        }
    }
    private string BuildChatStateInstallExpression()
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

    private async Task<ChatPageState> ReadChatStateCoreAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(tab, ChatStateReadExpression, cancellationToken, false);
        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, BuildChatStateInstallExpression(), cancellationToken, false);

        RecordAutoFollowState(tab, value);
        return new ChatPageState(
            value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0,
            value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(),
            value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("globalRateLimitText", out var rateLimit) ? rateLimit.GetString() ?? string.Empty : string.Empty);
    }
    private int IncrementChatStateTransportFailures(ChromeTab tab)
    {
        var key = GetChatStateFailureKey(tab);
        lock (_chatStateFailureSync)
        {
            var next = _chatStateTransportFailures.TryGetValue(key, out var current) ? checked(current + 1) : 1;
            _chatStateTransportFailures[key] = next;
            return next;
        }
    }
    private void ResetChatStateTransportFailures(ChromeTab tab)
    {
        var key = GetChatStateFailureKey(tab);
        lock (_chatStateFailureSync) _chatStateTransportFailures.Remove(key);
    }
    private static string GetChatStateFailureKey(ChromeTab tab)
        => !string.IsNullOrWhiteSpace(tab.Url) ? tab.Url : tab.Id;
    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);
        try
        {
            var replacement = await TryFindConversationTabAsync(tab.Url, cancellationToken);
            if (replacement is not null)
            {
                if (await RefreshConversationTabAsync(replacement, cancellationToken))
                {
                    RebindTab(tab, replacement);
                    return true;
                }

                replacement = await ReopenConversationTabAsync(tab.Url, replacement, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            var liveTabs = await TryGetLiveTabsAsync(cancellationToken);
            if (liveTabs is { Count: > 0 })
            {
                replacement = await ReopenConversationTabAsync(tab.Url, tab, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            liveTabs = await WaitForLiveTabsAfterTransportFailureAsync(cancellationToken);
            if (liveTabs is { Count: > 0 })
            {
                replacement = liveTabs.FirstOrDefault(candidate =>
                    RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)
                    && ChatGptConversationIdentity.IsSame(tab.Url, candidate.Url));

                if (replacement is not null && await RefreshConversationTabAsync(replacement, cancellationToken))
                {
                    RebindTab(tab, replacement);
                    return true;
                }

                replacement = await ReopenConversationTabAsync(tab.Url, replacement ?? tab, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            var restoreHidden = _monitorChromeHidden;
            await CloseAllMonitorTabsAsync(cancellationToken);
            LaunchMonitorChrome(tab.Url);

            replacement = await WaitForConversationTabAsync(tab.Url, cancellationToken);
            if (replacement is null)
            {
                liveTabs = await TryGetLiveTabsAsync(cancellationToken);
                if (liveTabs is not { Count: > 0 })
                    return false;
                replacement = await CreateTabAsync(tab.Url, cancellationToken);
            }

            if (!await WaitForReadableConversationStateAsync(replacement, cancellationToken))
                return false;

            RebindTab(tab, replacement);
            if (restoreHidden)
                await HideMonitorChromeAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "ChromeDevToolsService.AutoRecoverMonitorTab", null, tab.Id, tab.Title);
            return false;
        }
        finally
        {
            _monitorBrowserRecoveryGate.Release();
        }
    }

    private async Task<bool> RefreshConversationTabAsync(ChromeTab conversationTab, CancellationToken cancellationToken)
    {
        try
        {
            _sessionPool.Invalidate(conversationTab.Id);
            await ReloadTabAsync(conversationTab, cancellationToken);
            return await WaitForReadableConversationStateAsync(conversationTab, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            return false;
        }
    }

    private async Task<ChromeTab?> ReopenConversationTabAsync(string conversationUrl, ChromeTab? staleTab, CancellationToken cancellationToken)
    {
        ChromeTab? reopened = null;
        try
        {
            reopened = await CreateTabAsync(conversationUrl, cancellationToken);
            if (!await WaitForReadableConversationStateAsync(reopened, cancellationToken))
            {
                await CloseTabAsync(reopened, cancellationToken);
                return null;
            }

            if (staleTab is not null && !string.Equals(staleTab.Id, reopened.Id, StringComparison.Ordinal))
                await CloseTabAsync(staleTab, cancellationToken);

            return reopened;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (reopened is not null) await CloseTabAsync(reopened, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            if (reopened is not null) await CloseTabAsync(reopened, CancellationToken.None);
            return null;
        }
    }

    private async Task<bool> WaitForReadableConversationStateAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await ReadChatStateCoreAsync(tab, cancellationToken);
                if (state.AssistantCount > 0
                    || state.IsGenerating
                    || !string.IsNullOrWhiteSpace(state.LastAssistantText)
                    || !string.IsNullOrWhiteSpace(state.ErrorText))
                    return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private async Task<List<ChromeTab>?> TryGetLiveTabsAsync(CancellationToken cancellationToken)
    {
        try { return await GetTabsAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex)) { return null; }
    }
    private async Task<List<ChromeTab>?> WaitForLiveTabsAfterTransportFailureAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MonitorRecoveryEndpointGraceAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(MonitorRecoveryEndpointGraceDelayMs, cancellationToken);
            var tabs = await TryGetLiveTabsAsync(cancellationToken);
            if (tabs is { Count: > 0 })
                return tabs;
        }

        return null;
    }
    private async Task<ChromeTab?> TryFindConversationTabAsync(string conversationUrl, CancellationToken cancellationToken)
    {
        var tabs = await TryGetLiveTabsAsync(cancellationToken);
        return tabs?.FirstOrDefault(candidate =>
            RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)
            && ChatGptConversationIdentity.IsSame(conversationUrl, candidate.Url));
    }
    private async Task<ChromeTab?> WaitForConversationTabAsync(string conversationUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tab = await TryFindConversationTabAsync(conversationUrl, cancellationToken);
            if (tab is not null) return tab;
            await Task.Delay(250, cancellationToken);
        }
        return null;
    }
    private void RebindTab(ChromeTab current, ChromeTab replacement)
    {
        var staleId = current.Id;
        if (!string.IsNullOrWhiteSpace(staleId) && !string.Equals(staleId, replacement.Id, StringComparison.Ordinal))
            _sessionPool.Invalidate(staleId);

        current.Id = replacement.Id;
        current.Title = replacement.Title;
        current.Url = replacement.Url;
        current.Type = replacement.Type;
        current.WebSocketDebuggerUrl = replacement.WebSocketDebuggerUrl;
    }
    private static bool IsRecoverableMonitorTransportException(Exception ex)
        => ex is WebSocketException
           || ex is IOException
           || ex is TimeoutException
           || ex is TaskCanceledException
           || ex is HttpRequestException
           || ex.Message.Contains("Chrome closed the DevTools connection", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("session was invalidated", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("Inspected target navigated or closed", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("connection was forcibly closed", StringComparison.OrdinalIgnoreCase);
    private static bool IsExpectedBrowserCloseDisconnect(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is WebSocketException or ObjectDisposedException)
                return true;
        }

        return ex.Message.Contains("Chrome closed the DevTools connection", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("connection was forcibly closed", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ComposerReadinessSnapshot> ReadComposerReadinessAsync(
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var readiness = await EvaluateAsync(tab, ChatComposerReadinessScript.Expression, cancellationToken, false);
        var chatState = await ReadChatStateCoreAsync(tab, cancellationToken);

        return new ComposerReadinessSnapshot(
            IsGenerating: (readiness.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean()) || chatState.IsGenerating,
            EditorPresent: readiness.TryGetProperty("editorPresent", out var editorPresentElement) && editorPresentElement.GetBoolean(),
            EditorEnabled: readiness.TryGetProperty("editorEnabled", out var editorEnabledElement) && editorEnabledElement.GetBoolean(),
            SendButtonPresent: readiness.TryGetProperty("sendButtonPresent", out var sendPresentElement) && sendPresentElement.GetBoolean(),
            SendButtonEnabled: readiness.TryGetProperty("sendButtonEnabled", out var sendEnabledElement) && sendEnabledElement.GetBoolean(),
            HasRenderedError: !string.IsNullOrWhiteSpace(chatState.ErrorText));
    }

    private async Task<ComposerAutomationDecision> ReadComposerDecisionAsync(
        ChromeTab tab,
        bool requireSendReady,
        CancellationToken cancellationToken)
    {
        var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
        return requireSendReady
            ? ChatComposerInterlockPolicy.DecideBeforeSubmit(
                readiness.IsGenerating,
                readiness.EditorPresent,
                readiness.EditorEnabled,
                readiness.SendButtonPresent,
                readiness.SendButtonEnabled,
                readiness.HasRenderedError)
            : ChatComposerInterlockPolicy.DecideBeforeEditorMutation(
                readiness.IsGenerating,
                readiness.EditorPresent,
                readiness.EditorEnabled,
                readiness.HasRenderedError);
    }

    private async Task<bool> ComposerEditorMatchesExpectedAsync(
        ChromeTab tab,
        string expected,
        CancellationToken cancellationToken)
    {
        var expectedLiteral = JsonSerializer.Serialize(expected.Trim());
        var expression = $$"""
        (() => {
          const expected = {{expectedLiteral}};
          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');
          if (!editor) return false;
          const text = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
            ? editor.value
            : (editor.innerText || editor.textContent || '');
          return (text || '').trim() === expected;
        })()
        """;
        var value = await EvaluateAsync(tab, expression, cancellationToken, false);
        return value.ValueKind == JsonValueKind.True;
    }

    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);
        try
        {
            var replacement = await TryFindConversationTabAsync(tab.Url, cancellationToken);
            if (replacement is null || !ChatGptConversationIdentity.IsSame(tab.Url, replacement.Url))
                return false;

            _sessionPool.Invalidate(replacement.Id);
            await ReloadTabAsync(replacement, cancellationToken);
            RebindTab(tab, replacement);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await TryRefreshTabBindingAsync(tab, cancellationToken);
                    var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                    if (!readiness.IsGenerating
                        && readiness.EditorPresent
                        && readiness.EditorEnabled
                        && !readiness.HasRenderedError)
                        return true;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
                {
                }

                await Task.Delay(250, cancellationToken);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
        {
            return false;
        }
        finally
        {
            _monitorBrowserRecoveryGate.Release();
        }
    }

    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
    {
        var preparationDecision = await ReadComposerDecisionAsync(tab, requireSendReady: false, cancellationToken);
        if (preparationDecision != ComposerAutomationDecision.ReadyToPrepare)
            return false;

        var textLiteral = JsonSerializer.Serialize(message);
        var setEditorExpression = $$"""
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
          editor.focus();
          if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) {
            const setter = Object.getOwnPropertyDescriptor(editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')?.set;
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
          return true;
        })()
        """;

        var editorPrepared = await EvaluateAsync(tab, setEditorExpression, cancellationToken, false);
        if (editorPrepared.ValueKind != JsonValueKind.True) return false;

        for (var readinessAttempt = 0; readinessAttempt < 6; readinessAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var submitDecision = await ReadComposerDecisionAsync(tab, requireSendReady: true, cancellationToken);
            if (submitDecision == ComposerAutomationDecision.ReadyToSend)
                break;
            if (submitDecision is ComposerAutomationDecision.DeferWhileGenerating or ComposerAutomationDecision.DeferForRenderedError)
                return false;
            if (readinessAttempt == 5) return false;
            await Task.Delay(150, cancellationToken);
        }

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

        var submitted = await EvaluateAsync(tab, submitExpression, cancellationToken, false);
        return submitted.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false)
    {
        var expected = message.Trim();
        if (expected.Length == 0)
        {
            VerifiedSendDiagnostics.Record("Rejected", "empty-message", 0);
            return false;
        }

        const int maxSubmitAttempts = 2;
        var receiptGrace = TimeSpan.FromSeconds(3);
        var maxUnacknowledgedReconciliation = TimeSpan.FromSeconds(90);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        VerifiedSendDiagnostics.Record("Baseline", "reading-baseline", 0);

        var before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        while (!before.Success && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken);
            before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        }

        if (!before.Success)
        {
            VerifiedSendDiagnostics.Record("FailedClosed", "baseline-unreadable", 0);
            return false;
        }

        if (string.Equals(before.LastText, expected, StringComparison.Ordinal))
        {
            var deliveryState = await GetChatStateAsync(tab, cancellationToken);
            if (MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
                    requireNewTurn,
                    before.Count,
                    deliveryState.AssistantCount,
                    deliveryState.IsGenerating))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "matching-tail-reused", 0);
                return true;
            }
        }

        DateTimeOffset? sendBlockedSinceUtc = null;
        DateTimeOffset? unacknowledgedSubmitSinceUtc = null;
        var stuckRefreshUsed = false;
        var submitAttempts = 0;

        // Before a physical submit the normal deadline still applies. Once a submit has an
        // unknown outcome, reconciliation gets a bounded liveness budget. Budget exhaustion
        // fails closed and never authorizes another physical submit.
        while (DateTimeOffset.UtcNow < deadline
               || (unacknowledgedSubmitSinceUtc is not null
                   && DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value < maxUnacknowledgedReconciliation))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (!current.Success)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "new-user-turn-observed", submitAttempts);
                return true;
            }

            // Before any physical submit an unexpected user turn is a real conflict. After an
            // unacknowledged submit, however, a reload/rebind can expose a partially hydrated turn
            // list. Let reconciliation require stable evidence instead of failing on one DOM read.
            if (current.Count != before.Count && unacknowledgedSubmitSinceUtc is null)
            {
                VerifiedSendDiagnostics.Record("FailedClosed", "unexpected-user-turn-change", submitAttempts);
                return false;
            }

            if (unacknowledgedSubmitSinceUtc is not null)
            {
                if (DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value < receiptGrace)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                try
                {
                    var pendingReadiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                    if (pendingReadiness.HasRenderedError)
                    {
                        VerifiedSendDiagnostics.Record("FailedClosed", "rendered-error-after-submit", submitAttempts);
                        return false;
                    }

                    if (pendingReadiness.IsGenerating)
                    {
                        // The composer was verified idle immediately before our physical submit. If the
                        // same conversation is now generating after that submit, the server accepted a
                        // user turn even when the user-message DOM receipt is late or temporarily absent.
                        // Treat this as read-only acceptance evidence; never click Send again.
                        VerifiedSendDiagnostics.Record("ReceiptConfirmed", "generation-after-submit", submitAttempts);
                        return true;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
                {
                    _sessionPool.Invalidate(tab.Id);
                    VerifiedSendDiagnostics.Record("AwaitingReceipt", "post-submit-state-unreadable", submitAttempts);
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                VerifiedSendDiagnostics.Record("Reconciling", "receipt-not-observed-after-grace", submitAttempts);
                var reconciliationRemaining = maxUnacknowledgedReconciliation
                    - (DateTimeOffset.UtcNow - unacknowledgedSubmitSinceUtc.Value);
                if (reconciliationRemaining <= TimeSpan.Zero)
                {
                    VerifiedSendDiagnostics.Record("FailedClosed", "post-submit-reconciliation-time-budget-exhausted", submitAttempts);
                    return false;
                }

                using var reconciliationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                reconciliationCts.CancelAfter(reconciliationRemaining);
                UnacknowledgedSubmitReconciliationResult reconciliation;
                try
                {
                    reconciliation = await ReconcileUnacknowledgedSubmitAsync(
                        tab,
                        expected,
                        before.Count,
                        reconciliationCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && reconciliationCts.IsCancellationRequested)
                {
                    VerifiedSendDiagnostics.Record("FailedClosed", "post-submit-reconciliation-time-budget-exhausted", submitAttempts);
                    return false;
                }

                if (reconciliation == UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed)
                {
                    VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-confirmed-after-refresh", submitAttempts);
                    return true;
                }

                if (reconciliation == UnacknowledgedSubmitReconciliationResult.RetryAuthorized)
                {
                    if (submitAttempts >= maxSubmitAttempts)
                    {
                        VerifiedSendDiagnostics.Record("FailedClosed", "retry-limit-reached-without-receipt", submitAttempts);
                        return false;
                    }

                    unacknowledgedSubmitSinceUtc = null;
                    sendBlockedSinceUtc = null;
                    VerifiedSendDiagnostics.Record("RetryAuthorized", "stable-absence-after-refresh", submitAttempts);
                    continue;
                }

                if (reconciliation == UnacknowledgedSubmitReconciliationResult.TransientInterruption)
                {
                    // Target/session replacement and machine/browser contention are liveness events,
                    // not proof that the submit failed. Keep the original operation in-flight and
                    // rebind/read again. Crucially, do not clear unacknowledgedSubmitSinceUtc here.
                    VerifiedSendDiagnostics.Record("Reconciling", "transient-transport-recovery", submitAttempts);
                    await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                VerifiedSendDiagnostics.Record("FailedClosed", "ambiguous-post-submit-reconciliation", submitAttempts);
                return false;
            }

            ComposerAutomationDecision preparationDecision;
            try
            {
                preparationDecision = await ReadComposerDecisionAsync(tab, requireSendReady: false, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                _sessionPool.Invalidate(tab.Id);
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (preparationDecision != ComposerAutomationDecision.ReadyToPrepare)
            {
                sendBlockedSinceUtc = null;
                await Task.Delay(500, cancellationToken);
                continue;
            }

            bool submitted;
            try
            {
                submitted = await SendChatMessageAsync(tab, message, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                // SendChatMessageAsync mutates the editor before the final Runtime.evaluate click.
                // A transport loss here has an unknown physical outcome, so reconcile before any retry.
                submitAttempts++;
                unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
                _sessionPool.Invalidate(tab.Id);
                VerifiedSendDiagnostics.Record("AwaitingReceipt", "transport-uncertain-submit", submitAttempts);
                await Task.Delay(250, cancellationToken);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "ChromeDevToolsService.SendChatMessageVerified", null, tab.Id, tab.Title);
                VerifiedSendDiagnostics.Record("FailedClosed", "nonrecoverable-send-exception", submitAttempts);
                return false;
            }

            if (!submitted)
            {
                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                if (readiness.IsPostGenerationSendBlocked)
                {
                    var now = DateTimeOffset.UtcNow;
                    sendBlockedSinceUtc ??= now;
                    var editorMatchesExpected = await ComposerEditorMatchesExpectedAsync(tab, expected, cancellationToken);
                    var blockedFor = now - sendBlockedSinceUtc.Value;

                    if (StuckComposerRecoveryPolicy.ShouldRefresh(
                            readiness,
                            editorMatchesExpected,
                            blockedFor,
                            stuckRefreshUsed))
                    {
                        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
                        if (receiptBeforeRefresh.Success
                            && receiptBeforeRefresh.Count > before.Count
                            && string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal))
                        {
                            VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-before-stuck-refresh", submitAttempts);
                            return true;
                        }

                        stuckRefreshUsed = true;
                        sendBlockedSinceUtc = null;
                        if (await RefreshStuckComposerAsync(tab, cancellationToken))
                        {
                            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
                            if (receiptAfterRefresh.Success
                                && receiptAfterRefresh.Count > before.Count
                                && string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal))
                            {
                                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "receipt-after-stuck-refresh", submitAttempts);
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    sendBlockedSinceUtc = null;
                }

                await Task.Delay(500, cancellationToken);
                continue;
            }

            submitAttempts++;
            unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;
            VerifiedSendDiagnostics.Record("AwaitingReceipt", "physical-submit-unacknowledged", submitAttempts);

            await Task.Delay(300, cancellationToken);
            var after = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (after.Success && after.Count > before.Count && string.Equals(after.LastText, expected, StringComparison.Ordinal))
            {
                VerifiedSendDiagnostics.Record("ReceiptConfirmed", "immediate-user-turn-observed", submitAttempts);
                return true;
            }
        }

        VerifiedSendDiagnostics.Record(
            "FailedClosed",
            unacknowledgedSubmitSinceUtc is null
                ? "verified-send-deadline-without-receipt"
                : "post-submit-reconciliation-time-budget-exhausted",
            submitAttempts);
        return false;
    }

    private enum UnacknowledgedSubmitReconciliationResult
    {
        ReceiptConfirmed,
        RetryAuthorized,
        TransientInterruption,
        Ambiguous
    }

    private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync(
        ChromeTab tab,
        string expected,
        int baselineUserTurnCount,
        CancellationToken cancellationToken)
    {
        var originalUrl = tab.Url;
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(originalUrl))
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;

        var receiptBeforeRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        if (!receiptBeforeRefresh.Success)
            return UnacknowledgedSubmitReconciliationResult.TransientInterruption;
        if (receiptBeforeRefresh.Count > baselineUserTurnCount
            && string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal))
            return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
        // Do not classify a single pre-refresh count mismatch as a conflict. Target replacement
        // can briefly expose a partial turn list; the post-refresh loop below requires two stable
        // identical unexpected reads before returning Ambiguous.

        if (!await RefreshStuckComposerAsync(tab, cancellationToken))
            return UnacknowledgedSubmitReconciliationResult.TransientInterruption;
        if (!ChatGptConversationIdentity.IsSame(originalUrl, tab.Url))
            return UnacknowledgedSubmitReconciliationResult.Ambiguous;

        var stableAbsenceReads = 0;
        var stableUnexpectedReads = 0;
        var lastUnexpectedCount = -1;
        var lastUnexpectedText = string.Empty;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receiptAfterRefresh = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            var observation = MonitorDeliveryRecoveryPolicy.ClassifyPostRefreshUserTurn(
                receiptAfterRefresh.Success,
                baselineUserTurnCount,
                receiptAfterRefresh.Count,
                receiptAfterRefresh.LastText,
                expected);

            if (observation == PostRefreshUserTurnObservation.ReceiptConfirmed)
                return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;

            if (observation == PostRefreshUserTurnObservation.Hydrating)
            {
                stableAbsenceReads = 0;
                stableUnexpectedReads = 0;
                lastUnexpectedCount = -1;
                lastUnexpectedText = string.Empty;
                await Task.Delay(400, cancellationToken);
                continue;
            }

            if (observation == PostRefreshUserTurnObservation.UnexpectedChange)
            {
                stableAbsenceReads = 0;
                if (receiptAfterRefresh.Count == lastUnexpectedCount
                    && string.Equals(receiptAfterRefresh.LastText, lastUnexpectedText, StringComparison.Ordinal))
                {
                    stableUnexpectedReads++;
                }
                else
                {
                    stableUnexpectedReads = 1;
                    lastUnexpectedCount = receiptAfterRefresh.Count;
                    lastUnexpectedText = receiptAfterRefresh.LastText;
                }

                if (stableUnexpectedReads >= 2)
                    return UnacknowledgedSubmitReconciliationResult.Ambiguous;

                await Task.Delay(400, cancellationToken);
                continue;
            }

            stableUnexpectedReads = 0;
            lastUnexpectedCount = -1;
            lastUnexpectedText = string.Empty;

            try
            {
                var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);
                if (readiness.IsGenerating)
                    return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;
                if (readiness.HasRenderedError)
                    return UnacknowledgedSubmitReconciliationResult.Ambiguous;
                if (!readiness.EditorPresent || !readiness.EditorEnabled)
                {
                    stableAbsenceReads = 0;
                    await Task.Delay(400, cancellationToken);
                    continue;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                stableAbsenceReads = 0;
                _sessionPool.Invalidate(tab.Id);
                await Task.Delay(400, cancellationToken);
                continue;
            }

            stableAbsenceReads++;
            if (stableAbsenceReads >= 2)
                return UnacknowledgedSubmitReconciliationResult.RetryAuthorized;

            await Task.Delay(400, cancellationToken);
        }

        // Exhausting hydration/transport observations without stable conflicting evidence
        // is not a user-turn conflict. Keep the original submit under reconciliation.
        return UnacknowledgedSubmitReconciliationResult.TransientInterruption;
    }

    private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await GetUserMessageSnapshotAsync(tab, cancellationToken);
            return (true, snapshot.Count, snapshot.LastText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            _sessionPool.Invalidate(tab.Id);
            await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);
            return (false, 0, string.Empty);
        }
    }
    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        RuntimeFlightRecorder.Record("Browser", "BindingRefreshRequested", "started", "stable-target-search", tabId: tab.Id, conversationRef: tab.Url);
        try
        {
            var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);
            var current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);
            if (current is not null)
            {
                RebindTab(tab, current);
                RuntimeFlightRecorder.Record("Browser", "BindingRefreshed", "bound", "target-rebound", tabId: tab.Id, conversationRef: tab.Url);
            }
            else
            {
                RuntimeFlightRecorder.Record("Browser", "BindingRefreshed", "missing", "target-not-found", tabId: tab.Id, conversationRef: tab.Url);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RuntimeFlightRecorder.Record("Browser", "BindingRefreshCompleted", "cancelled", "operator-or-shutdown", tabId: tab.Id, conversationRef: tab.Url);
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            RuntimeFlightRecorder.Record("Browser", "BindingRefreshCompleted", "failed", ex.GetType().Name, tabId: tab.Id, conversationRef: tab.Url);
        }
    }
    private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync(ChromeTab tab, CancellationToken cancellationToken) { const string expression = """ (() => { const messages = [...document.querySelectorAll('[data-message-author-role="user"]')]; const last = messages.length ? (messages[messages.length - 1].innerText || messages[messages.length - 1].textContent || '').trim() : ''; return { count: messages.length, lastText: last }; })() """; var value = await EvaluateAsync(tab, expression, cancellationToken, false); var count = value.TryGetProperty("count", out var c) ? c.GetInt32() : 0; var last = value.TryGetProperty("lastText", out var t) ? t.GetString() ?? string.Empty : string.Empty; return (count, last); }
    public async Task ReloadTabAsync(ChromeTab tab, CancellationToken cancellationToken = default) => await SendCommandAsync(tab, "Page.reload", new { ignoreCache = false }, cancellationToken);
    private async Task<ChromeTab?> TryGetBrowserTargetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/version", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsElement)) return null;
            var webSocketDebuggerUrl = wsElement.GetString();
            if (string.IsNullOrWhiteSpace(webSocketDebuggerUrl)) return null;
            return new ChromeTab
            {
                Id = BrowserSessionId,
                Title = "GPTDeskTop Monitor Browser",
                Url = _config.DebuggingBaseUrl,
                Type = "browser",
                WebSocketDebuggerUrl = webSocketDebuggerUrl
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
    private async Task<JsonElement> EvaluateAsync(ChromeTab tab, string expression, CancellationToken cancellationToken, bool awaitPromise) { for (var attempt = 1; attempt <= 3; attempt++) { try { return await SendCommandAsync(tab, "Runtime.evaluate", new { expression, returnByValue = true, awaitPromise, userGesture = true }, cancellationToken, true); } catch (InvalidOperationException ex) when (IsTransientPromiseCollected(ex) && attempt < 3) { await Task.Delay(120 * attempt, cancellationToken); } } throw new InvalidOperationException("Runtime.evaluate failed after transient retry attempts."); }
    private static bool IsTransientPromiseCollected(Exception ex) => ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase);
    private static bool IsProcessRunning(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; }
        catch { return false; }
    }
    private Task<JsonElement> SendCommandAsync(ChromeTab tab, string method, object parameters, CancellationToken cancellationToken, bool extractRuntimeValue = false)
        => _sessionPool.SendCommandAsync(tab, method, parameters, cancellationToken, extractRuntimeValue);
    private static string FindChromePath() { var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe") }; var chrome = candidates.FirstOrDefault(File.Exists); if (chrome is null) throw new FileNotFoundException("Google Chrome was not found. Install Chrome or update the configured path."); return chrome; }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
}
