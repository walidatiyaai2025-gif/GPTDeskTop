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
  const version = 3;
  const unacceptedArmTimeoutMs = 12000;
  const existing = window[key];
  if (existing?.version === version) {
    try { existing.refreshLifecycle?.(); } catch { }
    return {
      installed: true,
      blockedCount: existing.blockedCount || 0,
      recoveredCount: existing.recoveredCount || 0,
      armed: !!existing.armed,
      accepted: !!existing.accepted
    };
  }
  try { existing?.dispose?.(); } catch { }

  const visible = element => {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const userCount = () => document.querySelectorAll('[data-message-author-role="user"]').length;
  const assistantCount = () => document.querySelectorAll('[data-message-author-role="assistant"]').length;
  // Keep the send-storm guard aligned with the main chat-state detector and composer-readiness probe.
  // Streaming DOM markers can survive hydration after a reply is visibly complete; treating them as
  // authoritative here leaves the guard permanently armed and suppresses every later Outbound turn.
  const isGenerating = () => {
    const stop = document.querySelector('button[data-testid="stop-button"]');
    return visible(stop);
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
    recoveredCount: 0,
    armed: false,
    accepted: false,
    armedAt: 0,
    userCountAtSend: -1,
    assistantCountAtSend: -1,
    refreshLifecycle: null,
    clickHandler: null,
    keyHandler: null,
    dispose: null
  };

  const disarm = () => {
    state.armed = false;
    state.accepted = false;
    state.armedAt = 0;
    state.userCountAtSend = -1;
    state.assistantCountAtSend = -1;
  };

  const observeAcceptedTurn = () => {
    if (!state.armed || state.accepted) return;
    if (userCount() > state.userCountAtSend || assistantCount() > state.assistantCountAtSend)
      state.accepted = true;
  };

  const newAssistantTurnCompleted = () =>
    state.armed && state.accepted && assistantCount() > state.assistantCountAtSend && !isGenerating();

  const unacceptedAttemptExpired = () =>
    state.armed && !state.accepted && state.armedAt > 0 && Date.now() - state.armedAt >= unacceptedArmTimeoutMs;

  state.refreshLifecycle = () => {
    if (!state.armed) return;
    observeAcceptedTurn();
    if (newAssistantTurnCompleted()) {
      disarm();
      return;
    }
    // A capture-phase click can arm the guard even when ChatGPT never commits the user turn
    // (DOM replacement, navigation, or a transport/recovery race). Never let that pre-commit
    // state suppress Outbound forever. Accepted sends remain protected until their assistant turn ends.
    if (unacceptedAttemptExpired()) {
      state.recoveredCount++;
      disarm();
    }
  };

  const allowOrBlock = event => {
    state.refreshLifecycle();
    if (state.armed) {
      state.blockedCount++;
      event.preventDefault();
      event.stopImmediatePropagation();
      return false;
    }
    state.userCountAtSend = userCount();
    state.assistantCountAtSend = assistantCount();
    state.armedAt = Date.now();
    state.accepted = false;
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
  return { installed: true, blockedCount: 0, recoveredCount: 0, armed: false, accepted: false };
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
            var previousRecoveredCounts = new Dictionary<string, int>(StringComparer.Ordinal);
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
                        await InstallSendGuardAsync(sessions, tab, previousBlockedCounts, previousRecoveredCounts).ConfigureAwait(false);
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
        Dictionary<string, int> previousBlockedCounts,
        Dictionary<string, int> previousRecoveredCounts)
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
            var previousBlocked = previousBlockedCounts.TryGetValue(tab.Id, out var priorBlocked) ? priorBlocked : 0;
            if (blockedCount > previousBlocked)
                WriteEvent("send-storm-suppressed", null, blockedCount - previousBlocked);
            previousBlockedCounts[tab.Id] = blockedCount;

            var recoveredCount = result.TryGetProperty("recoveredCount", out var recoveredElement)
                && recoveredElement.ValueKind == JsonValueKind.Number
                    ? recoveredElement.GetInt32()
                    : 0;
            var previousRecovered = previousRecoveredCounts.TryGetValue(tab.Id, out var priorRecovered) ? priorRecovered : 0;
            if (recoveredCount > previousRecovered)
                WriteEvent("send-guard-auto-released", "UnacceptedTimeout", recoveredCount - previousRecovered);
            previousRecoveredCounts[tab.Id] = recoveredCount;
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
