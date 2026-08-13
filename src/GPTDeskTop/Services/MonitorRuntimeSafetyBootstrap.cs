using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Production-only safety supervisor for two failure modes that must not be allowed to run unbounded:
/// a dead loopback Chrome DevTools endpoint and duplicate physical submissions of the same pending turn.
/// </summary>
internal static class MonitorRuntimeSafetyBootstrap
{
    private const int EndpointFailureThreshold = 4;
    private const int EndpointProbeAttempts = 16;
    private static readonly TimeSpan EndpointPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EndpointProbeDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EndpointRecoveryCooldown = TimeSpan.FromSeconds(30);

    private const string InstallSendGuardExpression = """
(() => {
  const key = '__gptDesktopSendStormGuard';
  const version = 1;
  const existing = window[key];
  if (existing?.version === version) {
    return { installed: true, blockedCount: existing.blockedCount || 0, armed: !!existing.armed };
  }
  try { existing?.dispose?.(); } catch { }

  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const assistantCount = () => document.querySelectorAll('[data-message-author-role="assistant"]').length;
  const isGenerating = () => {
    const stop = document.querySelector('button[data-testid="stop-button"]');
    if (visible(stop)) return true;
    const assistants = document.querySelectorAll('[data-message-author-role="assistant"]');
    const last = assistants.length ? assistants[assistants.length - 1] : null;
    if (!last) return false;
    const selector = '[data-is-streaming="true"],[data-streaming="true"],.result-streaming';
    if (last.matches?.(selector) && visible(last)) return true;
    return [...last.querySelectorAll(selector)].some(visible);
  };
  const isSendButton = button => {
    if (!button) return false;
    if (button.getAttribute('data-testid') === 'send-button') return true;
    const label = (button.getAttribute('aria-label') || '').trim();
    return /^(send|send message|إرسال|إرسال الرسالة)$/i.test(label);
  };
  const isPromptEditor = target => !!target?.closest?.('#prompt-textarea,textarea[placeholder],[contenteditable="true"]');

  const state = {
    version,
    blockedCount: 0,
    armed: false,
    assistantCountAtSend: -1,
    clickHandler: null,
    keyHandler: null,
    dispose: null
  };

  const newAssistantTurnCompleted = () =>
    state.armed && assistantCount() > state.assistantCountAtSend && !isGenerating();

  const allowOrBlock = event => {
    if (newAssistantTurnCompleted()) state.armed = false;
    if (state.armed) {
      state.blockedCount++;
      event.preventDefault();
      event.stopImmediatePropagation();
      return false;
    }
    state.assistantCountAtSend = assistantCount();
    state.armed = true;
    return true;
  };

  state.clickHandler = event => {
    const button = event.target?.closest?.('button');
    if (!isSendButton(button)) return;
    allowOrBlock(event);
  };
  state.keyHandler = event => {
    if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return;
    if (!isPromptEditor(event.target)) return;
    allowOrBlock(event);
  };
  state.dispose = () => {
    window.removeEventListener('click', state.clickHandler, true);
    window.removeEventListener('keydown', state.keyHandler, true);
  };

  window.addEventListener('click', state.clickHandler, true);
  window.addEventListener('keydown', state.keyHandler, true);
  window[key] = state;
  return { installed: true, blockedCount: 0, armed: false };
})()
""";

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Runtime test hosts reference the production assembly. The supervisor must only run inside
        // the actual desktop executable, never under testhost/dotnet/IDE processes.
        var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (!string.Equals(processName, "GPTDeskTop", StringComparison.OrdinalIgnoreCase)) return;
        _ = Task.Run(RunAsync);
    }

    private static async Task RunAsync()
    {
        try
        {
            var chrome = AppConfig.Load().Chrome;
            if (!Uri.TryCreate(chrome.DebuggingBaseUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            {
                WriteEvent("safety-disabled-nonloopback", null, null);
                return;
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var sessions = new ChromeDevToolsSessionPool();
            var previousBlockedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var consecutiveEndpointFailures = 0;
            var nextRecoveryAllowedUtc = DateTimeOffset.MinValue;

            while (true)
            {
                try
                {
                    var tabs = await GetTabsAsync(httpClient, chrome).ConfigureAwait(false);
                    if (consecutiveEndpointFailures > 0)
                        WriteEvent("cdp-endpoint-restored", null, consecutiveEndpointFailures);
                    consecutiveEndpointFailures = 0;
                    sessions.Prune(tabs);

                    foreach (var tab in tabs.Where(IsChatGptPage))
                        await InstallSendGuardAsync(sessions, tab, previousBlockedCounts).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    consecutiveEndpointFailures++;
                    if (consecutiveEndpointFailures == 1)
                        WriteEvent("cdp-endpoint-unavailable", ex.GetType().Name, 1);

                    if (consecutiveEndpointFailures >= EndpointFailureThreshold
                        && DateTimeOffset.UtcNow >= nextRecoveryAllowedUtc)
                    {
                        nextRecoveryAllowedUtc = DateTimeOffset.UtcNow + EndpointRecoveryCooldown;
                        WriteEvent("cdp-recovery-started", ex.GetType().Name, consecutiveEndpointFailures);

                        if (!TryLaunchDedicatedMonitorChrome(chrome, out var launchFailure))
                        {
                            WriteEvent("cdp-recovery-launch-failed", launchFailure, consecutiveEndpointFailures);
                        }
                        else if (await WaitForEndpointAsync(httpClient, chrome).ConfigureAwait(false))
                        {
                            consecutiveEndpointFailures = 0;
                            WriteEvent("cdp-recovery-succeeded", null, 0);
                        }
                        else
                        {
                            WriteEvent("cdp-recovery-probe-failed", null, consecutiveEndpointFailures);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent("runtime-safety-loop-failure", ex.GetType().Name, null);
                }

                await Task.Delay(EndpointPollInterval).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            WriteEvent("runtime-safety-fatal", ex.GetType().Name, null);
        }
    }

    private static async Task InstallSendGuardAsync(
        ChromeDevToolsSessionPool sessions,
        ChromeTab tab,
        Dictionary<string, int> previousBlockedCounts)
    {
        using var commandCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var result = await sessions.SendCommandAsync(
                tab,
                "Runtime.evaluate",
                new { expression = InstallSendGuardExpression, returnByValue = true, awaitPromise = false },
                commandCts.Token,
                extractRuntimeValue: true).ConfigureAwait(false);

            var blockedCount = result.TryGetProperty("blockedCount", out var blockedElement)
                && blockedElement.ValueKind == JsonValueKind.Number
                    ? blockedElement.GetInt32()
                    : 0;
            var previous = previousBlockedCounts.TryGetValue(tab.Id, out var prior) ? prior : 0;
            if (blockedCount > previous)
                WriteEvent("send-storm-suppressed", null, blockedCount - previous);
            previousBlockedCounts[tab.Id] = blockedCount;
        }
        catch (OperationCanceledException)
        {
            sessions.Invalidate(tab.Id);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            sessions.Invalidate(tab.Id);
        }
        catch (Exception ex)
        {
            sessions.Invalidate(tab.Id);
            WriteEvent("send-guard-probe-failed", ex.GetType().Name, null);
        }
    }

    private static async Task<List<ChromeTab>> GetTabsAsync(HttpClient httpClient, ChromeConfig chrome)
    {
        using var response = await httpClient.GetAsync($"{chrome.DebuggingBaseUrl.TrimEnd('/')}/json/list").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var tabs = new List<ChromeTab>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase)) continue;
            tabs.Add(new ChromeTab
            {
                Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                Title = string.Empty,
                Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                Type = type,
                WebSocketDebuggerUrl = item.TryGetProperty("webSocketDebuggerUrl", out var ws) ? ws.GetString() ?? string.Empty : string.Empty
            });
        }
        return tabs;
    }

    private static bool IsChatGptPage(ChromeTab tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Id) || string.IsNullOrWhiteSpace(tab.WebSocketDebuggerUrl)) return false;
        if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri)) return false;
        return string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "www.chatgpt.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForEndpointAsync(HttpClient httpClient, ChromeConfig chrome)
    {
        for (var attempt = 0; attempt < EndpointProbeAttempts; attempt++)
        {
            await Task.Delay(EndpointProbeDelay).ConfigureAwait(false);
            try
            {
                _ = await GetTabsAsync(httpClient, chrome).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
            }
        }
        return false;
    }

    private static bool TryLaunchDedicatedMonitorChrome(ChromeConfig chrome, out string? failureType)
    {
        failureType = null;
        try
        {
            var chromePath = FindChromePath();
            if (chromePath is null)
            {
                failureType = "ChromeExecutableNotFound";
                return false;
            }

            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GPTDeskTop",
                "ChromeProfile");
            Directory.CreateDirectory(profilePath);
            var arguments = $"--remote-debugging-port={chrome.DebuggingPort} --user-data-dir=\"{profilePath}\" --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-features=CalculateNativeWinOcclusion --new-window \"{chrome.StartUrl}\"";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = arguments,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch (Exception ex)
        {
            failureType = ex.GetType().Name;
            return false;
        }
    }

    private static string? FindChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void WriteEvent(string eventName, string? failureType, int? count)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "monitor-runtime-safety.jsonl");
            var record = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                @event = eventName,
                failureType,
                count
            });
            File.AppendAllText(path, record + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // Safety telemetry must never affect monitor execution.
        }
    }
}
