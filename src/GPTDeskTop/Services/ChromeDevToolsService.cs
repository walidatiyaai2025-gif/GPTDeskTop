using System.Diagnostics;
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
    private const string ChatStateReadExpression = "window.__gptDesktopChatStateCache?.version === 3 ? window.__gptDesktopChatStateCache.read() : null";
    private const string ChatStateInstallExpression = """
(() => {
  const key = '__gptDesktopChatStateCache';
  const version = 3;
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
  const findErrorText = () => {
    const selectors = ['[role="alert"]', '[aria-live="assertive"]', '[data-testid*="error"]', '[data-testid*="retry"]'];
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        if (!visible(element)) continue;
        const text = (element.innerText || element.textContent || '').trim();
        if (text && errorPattern.test(text)) return text;
      }
    }
    return '';
  };

  const state = {
    version,
    dirty: true,
    snapshot: { assistantCount: 0, lastAssistantText: '', isGenerating: false, errorText: '' },
    observer: null,
    read: null
  };
  state.read = () => {
    if (!state.dirty) return state.snapshot;
    state.dirty = false;
    const messages = document.querySelectorAll('[data-message-author-role="assistant"]');
    const lastAssistant = messages.length ? messages[messages.length - 1] : null;
    const stopButton = findStopButton();
    const streamingSignal = hasStreamingSignal(lastAssistant);
    const isGenerating = !!stopButton || streamingSignal;
    const errorText = findErrorText();
    const last = !isGenerating && lastAssistant ? (lastAssistant.innerText || '').trim() : '';
    state.snapshot = { assistantCount: messages.length, lastAssistantText: last, isGenerating, errorText };
    return state.snapshot;
  };
  state.observer = new MutationObserver(() => { state.dirty = true; });
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
    public async Task<bool> CloseTabAsync(ChromeTab tab, CancellationToken cancellationToken = default) { try { using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/close/{Uri.EscapeDataString(tab.Id)}", cancellationToken); return response.IsSuccessStatusCode; } catch { return false; } finally { _sessionPool.Invalidate(tab.Id); } }
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
                    // Browser.close commonly succeeds by tearing down its own DevTools socket before
                    // a response is observable. That disconnect is expected close semantics, not a fault.
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
                // Process already exited between checks.
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
        // Prefer a native hide. Changing Browser.setWindowBounds to "minimized" over CDP at the
        // same time monitor sessions are polling can churn DevTools sessions and trigger recovery.
        var handle = await ResolveMonitorWindowHandleAsync(cancellationToken);
        if (handle != IntPtr.Zero)
        {
            _lastKnownWindowHandle = handle;
            ShowWindow(handle, SwHide);
            _monitorChromeHidden = true;
            return true;
        }

        // Startup/ownership edge case: retain the legacy CDP fallback only when no native handle exists.
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
    private async Task<ChatPageState> ReadChatStateCoreAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(tab, ChatStateReadExpression, cancellationToken, false);
        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, ChatStateInstallExpression, cancellationToken, false);

        return new ChatPageState(
            value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0,
            value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(),
            value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty);
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
                // First escalation level: keep the exact conversation/target, force a clean CDP
                // session, reload it, and give ChatGPT a bounded window to restore real content.
                if (await RefreshConversationTabAsync(replacement, cancellationToken))
                {
                    RebindTab(tab, replacement);
                    return true;
                }

                // Second escalation level: the refreshed tab never became readable. Open the exact
                // saved /c/{conversation-id} URL in a new target; close the stale target only after
                // the replacement proves it can expose conversation state.
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

            // Do not kill a healthy hidden browser because of a short DevTools transport blip.
            // Require the endpoint to remain unavailable across a bounded grace window first.
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

            // The endpoint stayed unavailable through the grace window. Now a real browser restart
            // is justified. Preserve the operator's hidden preference across that recovery.
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
            catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
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
    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
    {
        var textLiteral = JsonSerializer.Serialize(message);
        var setEditorExpression = $$"""
        (() => {
          const text = {{textLiteral}};
          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]') || document.querySelector('[contenteditable="true"]');
          if (!editor) return false;
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

        var editorReady = await EvaluateAsync(tab, setEditorExpression, cancellationToken, false);
        if (editorReady.ValueKind != JsonValueKind.True) return false;
        await Task.Delay(350, cancellationToken);

        const string submitExpression = """
        (() => {
          const visible = element => {
            if (!element) return false;
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
          };
          const sendButton = document.querySelector('button[data-testid="send-button"]') ||
            [...document.querySelectorAll('button')].find(button => {
              if (!visible(button)) return false;
              const label = button.getAttribute('aria-label') || '';
              return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label.trim());
            });
          if (sendButton && !sendButton.disabled && visible(sendButton)) {
            sendButton.click();
            return { clicked: true, fallbackReady: false };
          }

          const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]') || document.querySelector('[contenteditable="true"]');
          if (!editor) return { clicked: false, fallbackReady: false };
          editor.focus();
          const editorText = editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement
            ? editor.value
            : (editor.innerText || editor.textContent || '');
          if (!editorText.trim()) return { clicked: false, fallbackReady: false };

          const stopButton = document.querySelector('button[data-testid="stop-button"]');
          return { clicked: false, fallbackReady: !visible(stopButton) };
        })()
        """;

        var submitState = await EvaluateAsync(tab, submitExpression, cancellationToken, false);
        var clicked = submitState.TryGetProperty("clicked", out var clickedElement) && clickedElement.GetBoolean();
        if (clicked) return true;

        var fallbackReady = submitState.TryGetProperty("fallbackReady", out var fallbackElement) && fallbackElement.GetBoolean();
        if (!fallbackReady) return false;

        await SendCommandAsync(
            tab,
            "Input.dispatchKeyEvent",
            new
            {
                type = "rawKeyDown",
                key = "Enter",
                code = "Enter",
                windowsVirtualKeyCode = 13,
                nativeVirtualKeyCode = 13
            },
            cancellationToken);
        await SendCommandAsync(
            tab,
            "Input.dispatchKeyEvent",
            new
            {
                type = "keyUp",
                key = "Enter",
                code = "Enter",
                windowsVirtualKeyCode = 13,
                nativeVirtualKeyCode = 13
            },
            cancellationToken);
        return true;
    }
    public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false)
    {
        var expected = message.Trim();
        if (expected.Length == 0) return false;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        while (!before.Success && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken);
            before = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
        }

        if (!before.Success) return false;
        if (string.Equals(before.LastText, expected, StringComparison.Ordinal))
        {
            var deliveryState = await GetChatStateAsync(tab, cancellationToken);
            if (MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
                    requireNewTurn,
                    before.Count,
                    deliveryState.AssistantCount,
                    deliveryState.IsGenerating))
                return true;
        }

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (!current.Success)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal))
                return true;

            try
            {
                if (!await SendChatMessageAsync(tab, message, cancellationToken))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
            {
                // Runtime.evaluate can time out after the page already handled the click. Retire the
                // broken transport and verify the DOM on the next pass before attempting another send.
                _sessionPool.Invalidate(tab.Id);
                await Task.Delay(250, cancellationToken);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "ChromeDevToolsService.SendChatMessageVerified", null, tab.Id, tab.Title);
            }

            await Task.Delay(300, cancellationToken);
            var after = await TryGetUserMessageSnapshotAsync(tab, cancellationToken);
            if (after.Success && after.Count > before.Count && string.Equals(after.LastText, expected, StringComparison.Ordinal))
                return true;
        }
        return false;
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
            // A timed-out CDP command marks its session broken. After the first ChatGPT message the
            // target may also have navigated to /c/{id}; refresh the target metadata/WebSocket before
            // the next verification so we do not keep reconnecting to the stale debugger URL.
            _sessionPool.Invalidate(tab.Id);
            await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);
            return (false, 0, string.Empty);
        }
    }
    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        try
        {
            var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);
            var current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);
            if (current is not null)
                RebindTab(tab, current);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            // The next verification loop retries target discovery; no duplicate send is issued first.
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