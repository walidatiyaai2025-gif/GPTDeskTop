using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Legacy probe command retained for CI compatibility. The probe now verifies that
/// elapsed time never refreshes a healthy/generating ChatGPT tab, even beyond the
/// former 30-second watchdog threshold, while an explicit current error still causes
/// exactly one error-driven refresh of only the affected tab.
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
                var outputPath = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "GPTDeskTop-passive-wait-probe.json");
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
            throw new ArgumentException("Passive-wait probe requires an output path.");

        var outputPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var probeRoot = Path.Combine(Path.GetTempPath(), "GPTDeskTop.PassiveWaitProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeRoot);
        var slowPath = Path.Combine(probeRoot, "slow.html");
        var errorPath = Path.Combine(probeRoot, "error.html");
        await File.WriteAllTextAsync(slowPath, SlowPage()).ConfigureAwait(false);
        await File.WriteAllTextAsync(errorPath, ErrorPage()).ConfigureAwait(false);

        var slowUrl = new Uri(slowPath).AbsoluteUri;
        var errorUrl = new Uri(errorPath).AbsoluteUri;
        var port = ReserveLoopbackPort();
        var chromeConfig = new ChromeConfig
        {
            DebuggingPort = port,
            DebuggingBaseUrl = $"http://127.0.0.1:{port}",
            StartUrl = slowUrl
        };
        var monitoringConfig = new MonitoringConfig
        {
            PollIntervalMilliseconds = 250,
            StableResponseMilliseconds = 1000,
            DelayAfterSendMilliseconds = 250
        };

        var identityHandler = new ConversationIdentityChromeListHandler();
        using var httpClient = new HttpClient(identityHandler) { Timeout = TimeSpan.FromSeconds(5) };
        var chrome = new ChromeDevToolsService(httpClient, chromeConfig);
        var database = new LocalDatabase(Path.Combine(probeRoot, "passive-wait.db"));
        await database.InitializeAsync().ConfigureAwait(false);

        // Preserve the old 30-second value deliberately. The production monitor must
        // ignore it as an intervention trigger so this probe catches any regression.
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
            chromeProcess = LaunchIsolatedChrome(probeRoot, slowUrl, port);
            var slowPhysicalTab = await WaitForTabAsync(chrome, chromeProcess, slowUrl).ConfigureAwait(false);
            var errorPhysicalTab = await chrome.CreateTabAsync(errorUrl).ConfigureAwait(false);

            const string slowConversationUrl = "https://chatgpt.com/c/qa-passive-wait-slow";
            const string errorConversationUrl = "https://chatgpt.com/c/qa-passive-wait-error";
            identityHandler.Map(slowPhysicalTab.Id, slowConversationUrl);
            identityHandler.Map(errorPhysicalTab.Id, errorConversationUrl);

            var slowTab = WithConversationIdentity(slowPhysicalTab, slowConversationUrl);
            var errorTab = WithConversationIdentity(errorPhysicalTab, errorConversationUrl);

            var slowMonitor = await SaveMonitorAsync(database, "QA slow thinking tab", slowTab).ConfigureAwait(false);
            var errorMonitor = await SaveMonitorAsync(database, "QA explicit error tab", errorTab).ConfigureAwait(false);

            await monitorService.StartMonitorAsync(slowMonitor, slowTab).ConfigureAwait(false);
            await monitorService.StartMonitorAsync(errorMonitor, errorTab).ConfigureAwait(false);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(65);
            ChatPageState? slowState = null;
            ChatPageState? errorState = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                slowState = await chrome.GetChatStateAsync(slowTab).ConfigureAwait(false);
                errorState = await chrome.GetChatStateAsync(errorTab).ConfigureAwait(false);
                if (string.Equals(slowState.LastAssistantText, "slow-complete-load-1", StringComparison.Ordinal) &&
                    string.Equals(errorState.LastAssistantText, "error-recovered-load-2", StringComparison.Ordinal))
                    break;
                await Task.Delay(500).ConfigureAwait(false);
            }

            slowState ??= await chrome.GetChatStateAsync(slowTab).ConfigureAwait(false);
            errorState ??= await chrome.GetChatStateAsync(errorTab).ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);
            var slowFinal = await chrome.GetChatStateAsync(slowTab).ConfigureAwait(false);
            var errorFinal = await chrome.GetChatStateAsync(errorTab).ConfigureAwait(false);

            string[] activitySnapshot;
            lock (activities) activitySnapshot = activities.ToArray();

            var result = new PassiveWaitProbeResult
            {
                SlowMonitorId = slowMonitor.Id,
                ErrorMonitorId = errorMonitor.Id,
                SlowText = slowFinal.LastAssistantText,
                ErrorText = errorFinal.LastAssistantText,
                SlowIsGenerating = slowFinal.IsGenerating,
                ErrorIsGenerating = errorFinal.IsGenerating,
                SlowErrorRefreshActivityCount = activitySnapshot.Count(x => x.Contains("QA slow thinking tab", StringComparison.Ordinal) && x.Contains("Error saved. Refreshing only this tab", StringComparison.Ordinal)),
                ErrorRefreshActivityCount = activitySnapshot.Count(x => x.Contains("QA explicit error tab", StringComparison.Ordinal) && x.Contains("Error saved. Refreshing only this tab", StringComparison.Ordinal)),
                ElapsedTimeRefreshActivityCount = activitySnapshot.Count(x => x.Contains("No new response for", StringComparison.OrdinalIgnoreCase) || x.Contains("NoResponseRefresh", StringComparison.OrdinalIgnoreCase)),
                SlowStillRunning = monitorService.IsMonitorRunning(slowMonitor.Id),
                ErrorStillRunning = monitorService.IsMonitorRunning(errorMonitor.Id),
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

    private static async Task<SavedMonitor> SaveMonitorAsync(LocalDatabase database, string title, ChromeTab tab)
    {
        var monitor = new SavedMonitor
        {
            Title = title,
            Url = tab.Url,
            TabId = tab.Id,
            AutoReply = "qa-no-send",
            TimerSeconds = 1,
            ReplyDelaySeconds = 300,
            Enabled = true,
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

    private static string SlowPage() => """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>QA slow thinking monitor</title></head>
<body>
  <main data-message-author-role="assistant"></main>
  <div data-message-author-role="user">Historical quote only: Something went wrong yesterday, but it is not a current error.</div>
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

    private static string ErrorPage() => """
<!doctype html>
<html>
<head><meta charset="utf-8"><title>QA explicit error monitor</title></head>
<body>
  <main data-message-author-role="assistant"></main>
  <script>
    const key = 'gptdesktop-passive-wait-error-load-count';
    const count = Number(sessionStorage.getItem(key) || '0') + 1;
    sessionStorage.setItem(key, String(count));
    const target = document.querySelector('[data-message-author-role="assistant"]');
    if (count === 1) {
      target.textContent = 'error-wait-load-1';
      setTimeout(() => {
        const alert = document.createElement('div');
        alert.setAttribute('role', 'alert');
        alert.style.width = '360px';
        alert.style.height = '32px';
        alert.textContent = 'Something went wrong';
        document.body.appendChild(alert);
      }, 35000);
    } else {
      target.textContent = `error-recovered-load-${count}`;
    }
  </script>
</body>
</html>
""";

    private static Process LaunchIsolatedChrome(string probeRoot, string url, int port)
    {
        var chromePath = FindChromePath();
        var profilePath = Path.Combine(probeRoot, "ChromeProfile");
        Directory.CreateDirectory(profilePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = chromePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = probeRoot
        };
        startInfo.ArgumentList.Add("--headless=new");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--remote-debugging-port={port}");
        startInfo.ArgumentList.Add($"--user-data-dir={profilePath}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--disable-background-networking");
        startInfo.ArgumentList.Add("--disable-component-update");
        startInfo.ArgumentList.Add("--disable-sync");
        startInfo.ArgumentList.Add("--disable-extensions");
        startInfo.ArgumentList.Add("--disable-gpu");
        startInfo.ArgumentList.Add(url);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Chrome passive-wait QA process could not be started.");
    }

    private static async Task<ChromeTab> WaitForTabAsync(ChromeDevToolsService chrome, Process process, string expectedUrl)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException($"Chrome passive-wait QA process exited before CDP became available. ExitCode={process.ExitCode}.");
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
        throw new TimeoutException($"Chrome passive-wait QA tab was not available within 60 seconds.{(lastError is null ? string.Empty : $" Last error: {lastError.Message}")}");
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
            ?? throw new FileNotFoundException("Google Chrome was not found for the passive-wait QA probe.");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task WriteResultAsync(string path, PassiveWaitProbeResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed class ConversationIdentityChromeListHandler : DelegatingHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly object _sync = new();
        private readonly Dictionary<string, string> _conversationUrls = new(StringComparer.Ordinal);

        public ConversationIdentityChromeListHandler()
            : base(new HttpClientHandler())
        {
        }

        public void Map(string targetId, string conversationUrl)
        {
            lock (_sync)
                _conversationUrls[targetId] = conversationUrl;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                !string.Equals(request.RequestUri?.AbsolutePath, "/json/list", StringComparison.OrdinalIgnoreCase))
                return response;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var tabs = JsonSerializer.Deserialize<List<ChromeTab>>(json, JsonOptions);
            if (tabs is null || tabs.Count == 0) return response;

            Dictionary<string, string> mappings;
            lock (_sync)
                mappings = new Dictionary<string, string>(_conversationUrls, StringComparer.Ordinal);

            foreach (var tab in tabs)
            {
                if (mappings.TryGetValue(tab.Id, out var conversationUrl))
                    tab.Url = conversationUrl;
            }

            response.Content.Dispose();
            response.Content = new StringContent(JsonSerializer.Serialize(tabs), Encoding.UTF8, "application/json");
            return response;
        }
    }

    private sealed class PassiveWaitProbeResult
    {
        public long SlowMonitorId { get; init; }
        public long ErrorMonitorId { get; init; }
        public string SlowText { get; init; } = string.Empty;
        public string ErrorText { get; init; } = string.Empty;
        public bool SlowIsGenerating { get; init; }
        public bool ErrorIsGenerating { get; init; }
        public int SlowErrorRefreshActivityCount { get; init; }
        public int ErrorRefreshActivityCount { get; init; }
        public int ElapsedTimeRefreshActivityCount { get; init; }
        public bool SlowStillRunning { get; init; }
        public bool ErrorStillRunning { get; init; }
        public string[] Activity { get; init; } = [];
    }
}
