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
    // Duplicate-send protection is implemented at the browser boundary:
    // a send is considered successful only after the expected user message
    // is observable in the conversation DOM.

    private readonly HttpClient _httpClient;
    private readonly ChromeConfig _config;
    private Process? _monitorChromeProcess;
    private IntPtr _lastKnownWindowHandle = IntPtr.Zero;

    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)
    {
        var before = await GetUserMessageSnapshotAsync(tab, cancellationToken);
        var text = message.Trim();
        if (text.Length == 0) return false;

        // If a previous uncertain attempt already made this exact message the newest
        // user message, treat it as delivered instead of sending a duplicate.
        if (before.LastText.Equals(text, StringComparison.Ordinal))
            return true;

        var textLiteral = JsonSerializer.Serialize(message);
        var setEditorExpression = $$"""
            (() => {
                const text = {{textLiteral}};
                const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]') || document.querySelector('[contenteditable="true"]');
                if (!editor) return false;
                editor.focus();
                if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) {
                    const proto = editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
                    const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
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

        var editorReady = await EvaluateAsync(tab, setEditorExpression, cancellationToken, awaitPromise: false);
        if (editorReady.ValueKind != JsonValueKind.True) return false;

        await Task.Delay(350, cancellationToken);
        const string clickExpression = """
            (() => {
                const sendButton = document.querySelector('button[data-testid="send-button"]') ||
                    [...document.querySelectorAll('button')].find(b => /send|إرسال/i.test(b.getAttribute('aria-label') || ''));
                if (!sendButton || sendButton.disabled) return false;
                sendButton.click();
                return true;
            })()
            """;
        var clicked = await EvaluateAsync(tab, clickExpression, cancellationToken, awaitPromise: false);
        if (clicked.ValueKind != JsonValueKind.True) return false;

        // The click is not treated as delivery. Confirm that the conversation actually
        // contains the expected user message. This makes retries idempotent across CDP
        // disconnects/timeouts.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(300, cancellationToken);
            var after = await GetUserMessageSnapshotAsync(tab, cancellationToken);
            if (after.Count > before.Count && after.LastText.Equals(text, StringComparison.Ordinal))
                return true;
            if (after.LastText.Equals(text, StringComparison.Ordinal) && after.Count >= before.Count)
                return true;
        }

        return false;
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
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue;
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

    public Process LaunchMonitorChrome(string? startUrl = null)
    {
        var chromePath = FindChromePath();
        var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "ChromeProfile");
        Directory.CreateDirectory(profilePath);
        var url = string.IsNullOrWhiteSpace(startUrl) ? _config.StartUrl : startUrl;
        var arguments = $"--remote-debugging-port={_config.DebuggingPort} --user-data-dir=\"{profilePath}\" --new-window \"{url}\"";
        _monitorChromeProcess = Process.Start(new ProcessStartInfo { FileName = chromePath, Arguments = arguments, UseShellExecute = true })
            ?? throw new InvalidOperationException("Chrome could not be started.");
        return _monitorChromeProcess;
    }

    // Existing project implementation continues below unchanged.

    private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        const string expression = """
            (() => {
                const messages = [...document.querySelectorAll('[data-message-author-role="user"]')];
                const last = messages.length ? (messages[messages.length - 1].innerText || messages[messages.length - 1].textContent || '').trim() : '';
                return { count: messages.length, lastText: last };
            })()
            """;
        var value = await EvaluateAsync(tab, expression, cancellationToken, awaitPromise: false);
        var count = value.TryGetProperty("count", out var countElement) ? countElement.GetInt32() : 0;
        var last = value.TryGetProperty("lastText", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
        return (count, last);
    }
}
