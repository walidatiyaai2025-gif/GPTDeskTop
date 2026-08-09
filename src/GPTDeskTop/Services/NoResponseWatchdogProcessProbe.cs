using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Runs two real ChatGptMonitorService workers against deterministic local pages.
/// One tab remains stale and must be reloaded by the 30-second watchdog; the other
/// produces activity before the timeout and must not be reloaded.
/// </summary>
internal static class NoResponseWatchdogProcessProbe
{
    private const string Command = "--qa-no-response-probe";

    public static bool IsProbeCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!IsProbeCommand(args)) return -1;

        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            try
            {
                var outputPath = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "GPTDeskTop-no-response-probe.json");
                File.WriteAllText(outputPath + ".error.txt", ex.ToString());
            }
            catch
            {
            }
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("No-response probe requires an output path.");

        var outputPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var probeRoot = Path.Combine(Path.GetTempPath(), "GPTDeskTop.NoResponseProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeRoot);
        var stalePath = Path.Combine(probeRoot, "stale.html");
        var activePath = Path.Combine(probeRoot, "active.html");
        await File.WriteAllTextAsync(stalePath, StalePage()).ConfigureAwait(false);
        await File.WriteAllTextAsync(activePath, ActivePage()).ConfigureAwait(false);

        var staleUrl = new Uri(stalePath).AbsoluteUri;
        var activeUrl = new Uri(activePath).AbsoluteUri;
        var port = ReserveLoopbackPort();
        var chromeConfig = new ChromeConfig
        {
            DebuggingPort = port,
            DebuggingBaseUrl = $"http://127.0.0.1:{port}",
            StartUrl = staleUrl
        };
        var monitoringConfig = new MonitoringConfig
        {
            PollIntervalMilliseconds = 250,
            StableResponseMilliseconds = 1000,
            DelayAfterSendMilliseconds = 250
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var chrome = new ChromeDevToolsService(httpClient, chromeConfig);
        var database = new LocalDatabase(Path.Combine(probeRoot, "watchdog.db"));
        await database.InitializeAsync().ConfigureAwait(false);
        await database.SetSettingAsync("NoResponseRefreshSeconds", "30").ConfigureAwait(false);

        Process? chromeProcess = null;
        var monitorService = new ChatGptMonitorService(chrome, database, monitoringConfig);
        var activities = new List<string>();
        monitorService.Activity += (_, text) =>
        {
            lock (activities) activities.Add(text);
        };

        try
        {
            chromeProcess = LaunchIsolatedChrome(probeRoot, staleUrl, port);
            var stalePhysicalTab = await WaitForTabAsync(chrome, chromeProcess, staleUrl).ConfigureAwait(false);
            var activePhysicalTab = await chrome.CreateTabAsync(activeUrl).ConfigureAwait(false);

            // Keep the real local pages/WebSocket targets for deterministic browser behavior,
            // but exercise ChatGptMonitorService through the same stable conversation-identity
            // contract required in production.
            var staleTab = WithConversationIdentity(stalePhysicalTab, "https://chatgpt.com/c/qa-no-response-stale");
            var activeTab = WithConversationIdentity(activePhysicalTab, "https://chatgpt.com/c/qa-no-response-active");

            var staleMonitor = await SaveMonitorAsync(database, "QA stale tab", staleTab, enabled: true).ConfigureAwait(false);
            var activeMonitor = await SaveMonitorAsync(database, "QA active tab", activeTab, enabled: true).ConfigureAwait(false);

            await monitorService.StartMonitorAsync(staleMonitor, staleTab).ConfigureAwait(false);
            await monitorService.StartMonitorAsync(activeMonitor, activeTab).ConfigureAwait(false);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(55);
            ChatPageState? staleState = null;
            ChatPageState? activeState = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                staleState = await chrome.GetChatStateAsync(staleTab).ConfigureAwait(false);
                activeState = await chrome.GetChatStateAsync(activeTab).ConfigureAwait(false);
                if (string.Equals(staleState.LastAssistantText, "stale-load-2", StringComparison.Ordinal) &&
                    string.Equals(activeState.LastAssistantText, "active-response-load-1", StringComparison.Ordinal))
                    break;
                await Task.Delay(500).ConfigureAwait(false);
            }

            staleState ??= await chrome.GetChatStateAsync(staleTab).ConfigureAwait(false);
            activeState ??= await chrome.GetChatStateAsync(activeTab).ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);
            var staleFinal = await chrome.GetChatStateAsync(staleTab).ConfigureAwait(false);
            var activeFinal = await chrome.GetChatStateAsync(activeTab).ConfigureAwait(false);

            string[] activitySnapshot;
            lock (activities) activitySnapshot = activities.ToArray();

            var result = new NoResponseProbeResult
            {
                StaleMonitorId = staleMonitor.Id,
                ActiveMonitorId = activeMonitor.Id,
                StaleText = staleFinal.LastAssistantText,
                ActiveText = activeFinal.LastAssistantText,
                StaleReloadActivityCount = activitySnapshot.Count(x => x.Contains("No new response for 30s", StringComparison.Ordinal) && x.Contains("QA stale tab", StringComparison.Ordinal)),
                ActiveReloadActivityCount = activitySnapshot.Count(x => x.Contains("No new response for 30s", StringComparison.Ordinal) && x.Contains("QA active tab", StringComparison.Ordinal)),
                StaleStillRunning = monitorService.IsMonitorRunning(staleMonitor.Id),
                ActiveStillRunning = monitorService.IsMonitorRunning(activeMonitor.Id),
                Activity = activitySnapshot
            };
            await WriteResultAsync(outputPath, result).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            try { await monitorService.StopAllAsync().ConfigureAwait(false); } catch { }
            try { await chrome.CloseAllMonitorTabsAsync().ConfigureAwait(false); } catch { }
            try
            {
                if (chromeProcess is not null)
                {
                    chromeProcess.Refresh();
                    if (!chromeProcess.HasExited)
                    {
                        chromeProcess.Kill(entireProcessTree: true);
                        chromeProcess.WaitForExit(5000);
                    }
                }
            }
            catch { }
            try { Directory.Delete(probeRoot, recursive: true); } catch { }
        }
    }

    private static async Task<SavedMonitor> SaveMonitorAsync(LocalDatabase database, string title, ChromeTab tab, bool enabled)
    {
        var monitor = new SavedMonitor
        {
            Title = title,
            Url = tab.Url,
            TabId = tab.Id,
            AutoReply = "qa-no-send",
            TimerSeconds = 1,
            ReplyDelaySeconds = 300,
            Enabled = enabled,
            ModelRoutingEnabled = false,
            ConversationRotationEnabled = false
        };
        monitor.Id = await database.SaveMonitorAsync(monitor).ConfigureAwait(false);
        return monitor;
    }

    private static ChromeTab WithConversationIdentity(ChromeTab physicalTab, string conversationUrl) => new()
    {
        Id = physicalTab.Id,
        Title = physicalTab.Title,
        Url = conversationUrl,
        Type = physicalTab.Type,
        WebSocketDebuggerUrl = physicalTab.WebSocketDebuggerUrl
    };

    private static string StalePage() => """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>QA stale monitor</title></head>
<body>
  <main data-message-author-role="assistant"></main>
  <script>
    const key = 'gptdesktop-stale-load-count';
    const count = Number(sessionStorage.getItem(key) || '0') + 1;
    sessionStorage.setItem(key, String(count));
    document.querySelector('[data-message-author-role="assistant"]').textContent = `stale-load-${count}`;
  </script>
</body>
</html>
""";

    private static string ActivePage() => """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>QA active monitor</title></head>
<body>
  <main data-message-author-role="assistant"></main>
  <script>
    const key = 'gptdesktop-active-load-count';
    const count = Number(sessionStorage.getItem(key) || '0') + 1;
    sessionStorage.setItem(key, String(count));
    const target = document.querySelector('[data-message-author-role="assistant"]');
    target.textContent = `active-initial-load-${count}`;
    setTimeout(() => { target.textContent = `active-response-load-${count}`; }, 5000);
  </script>
</body>
</html>
""";

    private static Process LaunchIsolatedChrome(string probeRoot, string url, int port)
    {
        var chromePath = FindChromePath();
        var profilePath = Path.Combine(probeRoot, "ChromeProfile");
        Directory.CreateDirectory(profilePath);
        var arguments = string.Join(' ',
            "--remote-debugging-address=127.0.0.1",
            $"--remote-debugging-port={port}",
            $"--user-data-dir=\"{profilePath}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-sync",
            "--disable-gpu",
            $"--new-window \"{url}\"");
        return Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = probeRoot
        }) ?? throw new InvalidOperationException("Chrome no-response QA process could not be started.");
    }

    private static async Task<ChromeTab> WaitForTabAsync(ChromeDevToolsService chrome, Process process, string expectedUrl)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException($"Chrome no-response QA process exited before CDP became available. ExitCode={process.ExitCode}.");
            try
            {
                var tabs = await chrome.GetTabsAsync().ConfigureAwait(false);
                var tab = tabs.FirstOrDefault(x => string.Equals(x.Url, expectedUrl, StringComparison.OrdinalIgnoreCase));
                if (tab is not null) return tab;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        throw new TimeoutException($"Chrome no-response QA tab was not available within 60 seconds.{(lastError is null ? string.Empty : $" Last error: {lastError.Message}")}");
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
            ?? throw new FileNotFoundException("Google Chrome was not found for the no-response QA probe.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task WriteResultAsync(string path, NoResponseProbeResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed class NoResponseProbeResult
    {
        public long StaleMonitorId { get; init; }
        public long ActiveMonitorId { get; init; }
        public string StaleText { get; init; } = string.Empty;
        public string ActiveText { get; init; } = string.Empty;
        public int StaleReloadActivityCount { get; init; }
        public int ActiveReloadActivityCount { get; init; }
        public bool StaleStillRunning { get; init; }
        public bool ActiveStillRunning { get; init; }
        public string[] Activity { get; init; } = [];
    }
}
