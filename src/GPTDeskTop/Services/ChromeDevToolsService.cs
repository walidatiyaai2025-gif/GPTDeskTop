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
    private Process? _monitorChromeProcess;
    private IntPtr _lastKnownWindowHandle = IntPtr.Zero;

    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<List<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"{_config.DebuggingBaseUrl.TrimEnd('/')}/json/list", cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(json);
        var tabs = new List<ChromeTab>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                continue;

            tabs.Add(new ChromeTab
            {
                Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                Type = type,
                WebSocketDebuggerUrl = item.TryGetProperty("webSocketDebuggerUrl", out var ws) ? ws.GetString() ?? string.Empty : string.Empty
            });
        }

        return tabs.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Process LaunchMonitorChrome()
    {
        var chromePath = FindChromePath();
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPTDeskTop",
            "ChromeProfile");
        Directory.CreateDirectory(profilePath);

        var arguments = $"--remote-debugging-port={_config.DebuggingPort} --user-data-dir=\"{profilePath}\" --new-window \"{_config.StartUrl}\"";
        _monitorChromeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = arguments,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Chrome could not be started.");

        return _monitorChromeProcess;
    }

    public async Task<bool> HideMonitorChromeAsync(CancellationToken cancellationToken = default)
    {
        var handle = await ResolveMonitorWindowHandleAsync(cancellationToken);
        if (handle != IntPtr.Zero)
        {
            _lastKnownWindowHandle = handle;
            return ShowWindow(handle, SwHide);
        }

        return await SetBrowserWindowStateAsync("minimized", cancellationToken);
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
            return true;
        }

        return await SetBrowserWindowStateAsync("normal", cancellationToken);
    }

    private async Task<IntPtr> ResolveMonitorWindowHandleAsync(CancellationToken cancellationToken)
    {
        if (_monitorChromeProcess is not null)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (_monitorChromeProcess.HasExited)
                        break;
                    _monitorChromeProcess.Refresh();
                    if (_monitorChromeProcess.MainWindowHandle != IntPtr.Zero)
                        return _monitorChromeProcess.MainWindowHandle;
                }
                catch
                {
                    break;
                }
                await Task.Delay(100, cancellationToken);
            }
        }

        return _lastKnownWindowHandle != IntPtr.Zero && IsWindow(_lastKnownWindowHandle)
            ? _lastKnownWindowHandle
            : IntPtr.Zero;
    }

    private async Task<bool> SetBrowserWindowStateAsync(string state, CancellationToken cancellationToken)
    {
        var tabs = await GetTabsAsync(cancellationToken);
        var tab = tabs.FirstOrDefault();
        if (tab is null)
            return false;

        var windowResult = await SendCommandAsync(tab, "Browser.getWindowForTarget", new { targetId = tab.Id }, cancellationToken);
        if (!windowResult.TryGetProperty("windowId", out var windowIdElement))
            return false;

        var windowId = windowIdElement.GetInt32();
        await SendCommandAsync(tab, "Browser.setWindowBounds", new
        {
            windowId,
            bounds = new { windowState = state }
        }, cancellationToken);
        return true;
    }

    public async Task<ChatPageState> GetChatStateAsync(ChromeTab tab, CancellationToken cancellationToken = default)
    {
        const string expression = """
            (() => {
                const messages = [...document.querySelectorAll('[data-message-author-role="assistant"]')];
                const last = messages.length ? (messages[messages.length - 1].innerText || '').trim() : '';
                const stopButton = document.querySelector('button[data-testid="stop-button"]') ||
                    [...document.querySelectorAll('button')].find(b => /stop generating|stop|إيقاف/i.test(b.getAttribute('aria-label') || ''));

                const candidates = [
                    ...document.querySelectorAll('[role="alert"]'),
                    ...document.querySelectorAll('[data-testid*="error"]'),
                    ...document.querySelectorAll('[class*="error"]')
                ];
                const errorPattern = /something went wrong|there was an error|network error|failed to (generate|load)|unable to (generate|load)|error generating|حدث خطأ|خطأ في الشبكة|تعذر/i;
                let errorText = '';
                for (const element of candidates) {
                    const text = (element.innerText || element.textContent || '').trim();
                    if (text && errorPattern.test(text)) {
                        errorText = text;
                        break;
                    }
                }
                if (!errorText) {
                    const visibleText = (document.body?.innerText || '').slice(-12000);
                    const match = visibleText.match(/(Something went wrong[^\n]*|There was an error[^\n]*|Network error[^\n]*|Failed to generate[^\n]*|Unable to generate[^\n]*|حدث خطأ[^\n]*|خطأ في الشبكة[^\n]*|تعذر[^\n]*)/i);
                    errorText = match ? match[1].trim() : '';
                }

                return {
                    assistantCount: messages.length,
                    lastAssistantText: last,
                    isGenerating: !!stopButton,
                    errorText
                };
            })()
            """;

        var value = await EvaluateAsync(tab, expression, cancellationToken);
        return new ChatPageState(
            value.TryGetProperty("assistantCount", out var count) ? count.GetInt32() : 0,
            value.TryGetProperty("lastAssistantText", out var text) ? text.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("isGenerating", out var generating) && generating.GetBoolean(),
            value.TryGetProperty("errorText", out var error) ? error.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
    {
        var textLiteral = JsonSerializer.Serialize(message);
        var expression = $$"""
            (async () => {
                const text = {{textLiteral}};
                const editor = document.querySelector('#prompt-textarea') ||
                    document.querySelector('textarea[placeholder]') ||
                    document.querySelector('[contenteditable="true"]');
                if (!editor) return false;

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
                }

                await new Promise(r => setTimeout(r, 300));
                const sendButton = document.querySelector('button[data-testid="send-button"]') ||
                    [...document.querySelectorAll('button')].find(b => /send|إرسال/i.test(b.getAttribute('aria-label') || ''));
                if (!sendButton || sendButton.disabled) return false;
                sendButton.click();
                return true;
            })()
            """;

        var value = await EvaluateAsync(tab, expression, cancellationToken);
        return value.ValueKind == JsonValueKind.True;
    }

    public async Task ReloadTabAsync(ChromeTab tab, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(tab, "Page.reload", new { ignoreCache = false }, cancellationToken);
    }

    private async Task<JsonElement> EvaluateAsync(ChromeTab tab, string expression, CancellationToken cancellationToken)
    {
        return await SendCommandAsync(tab, "Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true,
            userGesture = true
        }, cancellationToken, extractRuntimeValue: true);
    }

    private static async Task<JsonElement> SendCommandAsync(
        ChromeTab tab,
        string method,
        object parameters,
        CancellationToken cancellationToken,
        bool extractRuntimeValue = false)
    {
        if (string.IsNullOrWhiteSpace(tab.WebSocketDebuggerUrl))
            throw new InvalidOperationException("The selected tab does not expose a DevTools WebSocket URL.");

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(tab.WebSocketDebuggerUrl), cancellationToken);

        var request = JsonSerializer.Serialize(new
        {
            id = 1,
            method,
            @params = parameters
        });

        var bytes = Encoding.UTF8.GetBytes(request);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);

        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("Chrome closed the DevTools connection.");

            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            var payload = Encoding.UTF8.GetString(stream.ToArray());
            stream.SetLength(0);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1)
                continue;

            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"Chrome DevTools error: {error}");

            if (!extractRuntimeValue)
                return root.TryGetProperty("result", out var commandResult)
                    ? commandResult.Clone()
                    : JsonDocument.Parse("null").RootElement.Clone();

            var resultElement = root.GetProperty("result").GetProperty("result");
            if (resultElement.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "error")
            {
                var description = resultElement.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : "JavaScript evaluation failed.";
                throw new InvalidOperationException(description);
            }

            if (!resultElement.TryGetProperty("value", out var value))
                return JsonDocument.Parse("null").RootElement.Clone();

            return value.Clone();
        }
    }

    private static string FindChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome was not found in the standard Windows install locations.");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
