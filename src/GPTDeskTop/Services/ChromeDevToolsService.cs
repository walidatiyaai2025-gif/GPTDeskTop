using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChromeDevToolsService
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private readonly HttpClient _httpClient;
    private readonly ChromeConfig _config;
    private readonly Func<ChromeTab, string, object, CancellationToken, bool, Task<JsonElement>> _commandSender;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;
    private Process? _monitorChromeProcess;
    private IntPtr _lastKnownWindowHandle = IntPtr.Zero;
    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config)
        : this(httpClient, config, SendCommandAsync, Task.Delay) { }

    internal ChromeDevToolsService(
        HttpClient httpClient,
        ChromeConfig config,
        Func<ChromeTab, string, object, CancellationToken, bool, Task<JsonElement>> commandSender,
        Func<TimeSpan, CancellationToken, Task> retryDelay)
    {
        _httpClient = httpClient;
        _config = config;
        _commandSender = commandSender;
        _retryDelay = retryDelay;
    }
    public async Task<List<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken = default) { using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/list", cancellationToken); response.EnsureSuccessStatusCode(); var json = await response.Content.ReadAsStringAsync(cancellationToken); using var document = JsonDocument.Parse(json); var tabs = new List<ChromeTab>(); foreach (var item in document.RootElement.EnumerateArray()) { var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty; if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue; tabs.Add(new ChromeTab { Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty, Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty, Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty, Type = type, WebSocketDebuggerUrl = item.TryGetProperty("webSocketDebuggerUrl", out var ws) ? ws.GetString() ?? string.Empty : string.Empty }); } return tabs.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList(); }
    public Process LaunchMonitorChrome(string? startUrl = null) { var chromePath = FindChromePath(); var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "ChromeProfile"); Directory.CreateDirectory(profilePath); var url = string.IsNullOrWhiteSpace(startUrl) ? _config.StartUrl : startUrl; var arguments = $"--remote-debugging-port={_config.DebuggingPort} --user-data-dir=\"{profilePath}\" --new-window \"{url}\""; _monitorChromeProcess = Process.Start(new ProcessStartInfo { FileName = chromePath, Arguments = arguments, UseShellExecute = true }) ?? throw new InvalidOperationException("Chrome could not be started."); return _monitorChromeProcess; }
    public async Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken = default) { var existing = await GetTabsAsync(cancellationToken); var controlTab = existing.FirstOrDefault() ?? throw new InvalidOperationException("Monitor Chrome has no controllable page."); var result = await SendCommandAsync(controlTab, "Target.createTarget", new { url }, cancellationToken); var targetId = result.TryGetProperty("targetId", out var id) ? id.GetString() : null; if (string.IsNullOrWhiteSpace(targetId)) throw new InvalidOperationException("Chrome did not return a target ID for the new tab."); for (var attempt = 0; attempt < 40; attempt++) { cancellationToken.ThrowIfCancellationRequested(); await Task.Delay(250, cancellationToken); var tabs = await GetTabsAsync(cancellationToken); var created = tabs.FirstOrDefault(t => string.Equals(t.Id, targetId, StringComparison.Ordinal)); if (created is not null) return created; } throw new TimeoutException("The new Chrome tab did not become ready in time."); }
    public Task<ChromeTab> CreateNewChatTabAsync(CancellationToken cancellationToken = default) => CreateTabAsync(_config.StartUrl, cancellationToken);
    public async Task<bool> CloseTabAsync(ChromeTab tab, CancellationToken cancellationToken = default) { try { using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/close/{Uri.EscapeDataString(tab.Id)}", cancellationToken); return response.IsSuccessStatusCode; } catch { return false; } }
    public async Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken = default) { List<ChromeTab> tabs; try { tabs = await GetTabsAsync(cancellationToken); } catch { return; } foreach (var tab in tabs) { cancellationToken.ThrowIfCancellationRequested(); await CloseTabAsync(tab, cancellationToken); } }
    public async Task<bool> TrySelectModelAsync(ChromeTab tab, string modelLabel, CancellationToken cancellationToken = default) { if (string.IsNullOrWhiteSpace(modelLabel) || string.Equals(modelLabel.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)) return true; var labelLiteral = JsonSerializer.Serialize(modelLabel.Trim()); var expression = $$""" (() => { const requested = {{labelLiteral}}.trim().toLowerCase(); const normalize = value => (value || '').replace(/\s+/g, ' ').trim().toLowerCase(); const visible = element => { const r = element.getBoundingClientRect(); const s = getComputedStyle(element); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; }; const elements = [...document.querySelectorAll('button,[role="button"],[role="menuitem"],[role="option"]')]; const modelButton = elements.find(e => { if (!visible(e)) return false; const label = normalize(e.getAttribute('aria-label')); const text = normalize(e.innerText || e.textContent); return /model|م\u0648\u062f\u064a\u0644|reasoning|thinking|instant/i.test(label + ' ' + text); }); if (modelButton) modelButton.click(); const items = [...document.querySelectorAll('[role="menuitem"],[role="option"],button')]; const target = items.find(e => visible(e) && normalize(e.innerText || e.textContent).includes(requested)); if (!target) return false; target.click(); return true; })() """; try { var result = await EvaluateAsync(tab, expression, cancellationToken, false); return result.ValueKind == JsonValueKind.True; } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, "ChromeDevToolsService.TrySelectModel", null, tab.Id, tab.Title); return false; } }
    public async Task<bool> HideMonitorChromeAsync(CancellationToken cancellationToken = default) { var changed = await SetAllBrowserWindowsStateAsync("minimized", cancellationToken); var handle = await ResolveMonitorWindowHandleAsync(cancellationToken); if (handle != IntPtr.Zero) { _lastKnownWindowHandle = handle; ShowWindow(handle, SwHide); changed = true; } return changed; }
    public async Task<bool> ShowMonitorChromeAsync(CancellationToken cancellationToken = default) { var changed = await SetAllBrowserWindowsStateAsync("normal", cancellationToken); var handle = _lastKnownWindowHandle; if (handle == IntPtr.Zero || !IsWindow(handle)) handle = await ResolveMonitorWindowHandleAsync(cancellationToken); if (handle != IntPtr.Zero) { _lastKnownWindowHandle = handle; ShowWindow(handle, SwShow); ShowWindow(handle, SwRestore); changed = true; } return changed; }
    private async Task<IntPtr> ResolveMonitorWindowHandleAsync(CancellationToken cancellationToken) { if (_monitorChromeProcess is not null) { for (var attempt = 0; attempt < 20; attempt++) { cancellationToken.ThrowIfCancellationRequested(); try { if (_monitorChromeProcess.HasExited) break; _monitorChromeProcess.Refresh(); if (_monitorChromeProcess.MainWindowHandle != IntPtr.Zero) return _monitorChromeProcess.MainWindowHandle; } catch { break; } await Task.Delay(100, cancellationToken); } } return _lastKnownWindowHandle != IntPtr.Zero && IsWindow(_lastKnownWindowHandle) ? _lastKnownWindowHandle : IntPtr.Zero; }
    private async Task<bool> SetAllBrowserWindowsStateAsync(string state, CancellationToken cancellationToken) { List<ChromeTab> tabs; try { tabs = await GetTabsAsync(cancellationToken); } catch { return false; } if (tabs.Count == 0) return false; var windowIds = new HashSet<int>(); foreach (var tab in tabs) { try { var windowResult = await SendCommandAsync(tab, "Browser.getWindowForTarget", new { targetId = tab.Id }, cancellationToken); if (windowResult.TryGetProperty("windowId", out var windowIdElement)) windowIds.Add(windowIdElement.GetInt32()); } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, "ChromeDevToolsService.GetWindowForTarget", null, tab.Id, tab.Title); } } var changed = false; foreach (var windowId in windowIds) { try { await SendCommandAsync(tabs[0], "Browser.setWindowBounds", new { windowId, bounds = new { windowState = state } }, cancellationToken); changed = true; } catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, $"ChromeDevToolsService.SetWindowState({state})"); } } return changed; }
    public async Task<ChatPageState> GetChatStateAsync(ChromeTab tab, CancellationToken cancellationToken = default)
    {
        const string expression = """
(() => {
  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const messages = [...document.querySelectorAll('[data-message-author-role="assistant"]')];
  const last = messages.length ? (messages[messages.length - 1].innerText || '').trim() : '';
  const testStopButton = document.querySelector('button[data-testid="stop-button"]');
  const stopButton = visible(testStopButton) ? testStopButton : [...document.querySelectorAll('button')].find(b => visible(b) && /stop generating|stop responding|stop|إيقاف/i.test(`${b.getAttribute('aria-label') || ''} ${b.getAttribute('title') || ''}`));
  const streamingSignal = [...document.querySelectorAll('[data-is-streaming="true"],[data-streaming="true"],.result-streaming,[aria-busy="true"]')].some(element => visible(element) && (element.closest('[data-message-author-role="assistant"]') || element.closest('form')));
  const candidates = [...document.querySelectorAll('[role="alert"]'), ...document.querySelectorAll('[aria-live="assertive"]'), ...document.querySelectorAll('[data-testid*="error"]'), ...document.querySelectorAll('[data-testid*="retry"]')];
  const errorPattern = /message delivery timed out|something went wrong|there was an error|network error|failed to (generate|load)|unable to (generate|load)|error generating|حدث خطأ|خطأ في الشبكة|تعذر/i;
  let errorText = '';
  for (const element of candidates) {
    if (!visible(element)) continue;
    const text = (element.innerText || element.textContent || '').trim();
    if (text && errorPattern.test(text)) { errorText = text; break; }
  }
  return { assistantCount: messages.length, lastAssistantText: last, isGenerating: !!stopButton || streamingSignal, errorText };
})()
""";
        var value = await EvaluateAsync(tab, expression, cancellationToken, false);
        return new ChatPageState(value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0, value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty, value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(), value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty);
    }
    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default) { var textLiteral = JsonSerializer.Serialize(message); var setEditorExpression = $$""" (() => { const text = {{textLiteral}}; const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]') || document.querySelector('[contenteditable="true"]'); if (!editor) return false; editor.focus(); if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) { const setter = Object.getOwnPropertyDescriptor(editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')?.set; setter?.call(editor, text); editor.dispatchEvent(new Event('input', { bubbles: true })); editor.dispatchEvent(new Event('change', { bubbles: true })); } else { const selection = window.getSelection(); const range = document.createRange(); range.selectNodeContents(editor); selection?.removeAllRanges(); selection?.addRange(range); document.execCommand('insertText', false, text); editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text })); } return true; })() """; var editorReady = await EvaluateAsync(tab, setEditorExpression, cancellationToken, false); if (editorReady.ValueKind != JsonValueKind.True) return false; await Task.Delay(350, cancellationToken); const string clickExpression = """ (() => { const sendButton = document.querySelector('button[data-testid="send-button"]') || [...document.querySelectorAll('button')].find(b => /send|إرسال/i.test(b.getAttribute('aria-label') || '')); if (!sendButton || sendButton.disabled) return false; sendButton.click(); return true; })() """; var clicked = await EvaluateAsync(tab, clickExpression, cancellationToken, false); return clicked.ValueKind == JsonValueKind.True; }
    public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
    {
        var expected = message.Trim(); if (expected.Length == 0) return false;
        var before = await GetUserMessageSnapshotAsync(tab, cancellationToken);
        if (string.Equals(before.LastText, expected, StringComparison.Ordinal)) return true;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var attempt = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested(); attempt++;
            try
            {
                var current = await GetUserMessageSnapshotAsync(tab, cancellationToken);
                if (current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal)) return true;
                if (!await SendChatMessageAsync(tab, message, cancellationToken)) { await Task.Delay(500, cancellationToken); continue; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "ChromeDevToolsService.SendChatMessageVerified", null, tab.Id, tab.Title);
            }
            await Task.Delay(300, cancellationToken);
            try
            {
                var after = await GetUserMessageSnapshotAsync(tab, cancellationToken);
                if (after.Count > before.Count && string.Equals(after.LastText, expected, StringComparison.Ordinal)) return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { ExceptionLogService.Log(ex, "ChromeDevToolsService.VerifySend", null, tab.Id, tab.Title); }
        }
        return false;
    }
    private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync(ChromeTab tab, CancellationToken cancellationToken) { const string expression = """ (() => { const messages = [...document.querySelectorAll('[data-message-author-role="user"]')]; const last = messages.length ? (messages[messages.length - 1].innerText || messages[messages.length - 1].textContent || '').trim() : ''; return { count: messages.length, lastText: last }; })() """; var value = await EvaluateAsync(tab, expression, cancellationToken, false); var count = value.TryGetProperty("count", out var c) ? c.GetInt32() : 0; var last = value.TryGetProperty("lastText", out var t) ? t.GetString() ?? string.Empty : string.Empty; return (count, last); }
    public async Task ReloadTabAsync(ChromeTab tab, CancellationToken cancellationToken = default) => await SendCommandAsync(tab, "Page.reload", new { ignoreCache = false }, cancellationToken);
    private async Task<JsonElement> EvaluateAsync(ChromeTab tab, string expression, CancellationToken cancellationToken, bool awaitPromise) { for (var attempt = 1; attempt <= 3; attempt++) { try { return await _commandSender(tab, "Runtime.evaluate", new { expression, returnByValue = true, awaitPromise, userGesture = true }, cancellationToken, true); } catch (InvalidOperationException ex) when (IsTransientPromiseCollected(ex) && attempt < 3) { await _retryDelay(TimeSpan.FromMilliseconds(120 * attempt), cancellationToken); } } throw new InvalidOperationException("Runtime.evaluate failed after transient retry attempts."); }
    private static bool IsTransientPromiseCollected(Exception ex) => ex.Message.Contains("Promise was collected", StringComparison.OrdinalIgnoreCase);
    private static async Task<JsonElement> SendCommandAsync(ChromeTab tab, string method, object parameters, CancellationToken cancellationToken, bool extractRuntimeValue = false) { if (string.IsNullOrWhiteSpace(tab.WebSocketDebuggerUrl)) throw new InvalidOperationException("The selected tab does not expose a DevTools WebSocket URL."); using var socket = new ClientWebSocket(); await socket.ConnectAsync(new Uri(tab.WebSocketDebuggerUrl), cancellationToken); var request = JsonSerializer.Serialize(new { id = 1, method, @params = parameters }); var bytes = Encoding.UTF8.GetBytes(request); await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken); var buffer = new byte[64 * 1024]; using var stream = new MemoryStream(); while (true) { var result = await socket.ReceiveAsync(buffer, cancellationToken); if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Chrome closed the DevTools connection."); stream.Write(buffer, 0, result.Count); if (!result.EndOfMessage) continue; var payload = Encoding.UTF8.GetString(stream.ToArray()); stream.SetLength(0); using var document = JsonDocument.Parse(payload); var root = document.RootElement; if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue; if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException($"Chrome DevTools error: {error}"); if (!extractRuntimeValue) return root.TryGetProperty("result", out var commandResult) ? commandResult.Clone() : JsonDocument.Parse("null").RootElement.Clone(); var resultElement = root.GetProperty("result").GetProperty("result"); if (resultElement.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "error") throw new InvalidOperationException(resultElement.TryGetProperty("description", out var d) ? d.GetString() : "JavaScript evaluation failed."); return resultElement.TryGetProperty("value", out var value) ? value.Clone() : JsonDocument.Parse("null").RootElement.Clone(); } }
    private static string FindChromePath() { var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe") }; var chrome = candidates.FirstOrDefault(File.Exists); if (chrome is null) throw new FileNotFoundException("Google Chrome was not found. Install Chrome or update the configured path."); return chrome; }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
}
