using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Windows/Chrome QA probe that verifies the production CDP client continues to
/// read a monitored page while the monitor Chrome window is hidden/minimized.
/// It intentionally uses a local deterministic HTML page instead of requiring a
/// logged-in external ChatGPT session.
/// </summary>
internal static class HiddenChromeProcessProbe
{
    private const string Command = "--qa-hidden-chrome-probe";
    private const string ExpectedText = "hidden-cdp-probe-alive";

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
                var outputPath = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(Path.GetTempPath(), "GPTDeskTop-hidden-chrome-probe.json");
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
        if (args.Length < 3)
            throw new ArgumentException("Hidden Chrome probe requires duration seconds and an output path.");
        if (!int.TryParse(args[1], out var durationSeconds))
            throw new ArgumentException("Hidden Chrome probe duration must be an integer number of seconds.");

        durationSeconds = Math.Clamp(durationSeconds, 5, 900);
        var outputPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var probeRoot = Path.Combine(Path.GetTempPath(), "GPTDeskTop.HiddenChromeProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeRoot);
        var htmlPath = Path.Combine(probeRoot, "probe.html");
        await File.WriteAllTextAsync(htmlPath, $"""
<!doctype html>
<html>
<head><meta charset="utf-8"><title>GPTDeskTop Hidden CDP Probe</title></head>
<body>
  <main data-message-author-role="assistant">{ExpectedText}</main>
</body>
</html>
""").ConfigureAwait(false);

        var port = ReserveLoopbackPort();
        var url = new Uri(htmlPath).AbsoluteUri;
        var config = new ChromeConfig
        {
            DebuggingPort = port,
            DebuggingBaseUrl = $"http://127.0.0.1:{port}",
            StartUrl = url
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var chrome = new ChromeDevToolsService(httpClient, config);
        Process? process = null;
        var successfulPolls = 0;
        var matchingPolls = 0;
        var failedPolls = 0;
        var lastText = string.Empty;
        var hideChanged = false;
        var showChanged = false;
        var stopwatch = new Stopwatch();

        try
        {
            process = chrome.LaunchMonitorChrome(url);
            var tab = await WaitForProbeTabAsync(chrome, url).ConfigureAwait(false);
            hideChanged = await chrome.HideMonitorChromeAsync().ConfigureAwait(false);

            stopwatch.Start();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(durationSeconds))
            {
                try
                {
                    var state = await chrome.GetChatStateAsync(tab).ConfigureAwait(false);
                    successfulPolls++;
                    lastText = state.LastAssistantText;
                    if (string.Equals(lastText, ExpectedText, StringComparison.Ordinal)) matchingPolls++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedPolls++;
                }

                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            stopwatch.Stop();

            showChanged = await chrome.ShowMonitorChromeAsync().ConfigureAwait(false);

            var result = new HiddenChromeProbeResult
            {
                RequestedSeconds = durationSeconds,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                ChromeProcessId = process.Id,
                HideChanged = hideChanged,
                ShowChanged = showChanged,
                SuccessfulPolls = successfulPolls,
                MatchingPolls = matchingPolls,
                FailedPolls = failedPolls,
                LastAssistantText = lastText,
                DebuggingPort = port
            };
            await WriteResultAsync(outputPath, result).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            try { await chrome.CloseAllMonitorTabsAsync().ConfigureAwait(false); } catch { }
            try
            {
                if (process is not null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch { }
            try { Directory.Delete(probeRoot, recursive: true); } catch { }
        }
    }

    private static async Task<ChromeTab> WaitForProbeTabAsync(ChromeDevToolsService chrome, string expectedUrl)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var tabs = await chrome.GetTabsAsync().ConfigureAwait(false);
                var tab = tabs.FirstOrDefault(x => string.Equals(x.Url, expectedUrl, StringComparison.OrdinalIgnoreCase))
                    ?? tabs.FirstOrDefault(x => x.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
                if (tab is not null) return tab;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Chrome probe tab was not available within 60 seconds.{(lastError is null ? string.Empty : $" Last error: {lastError.Message}")}");
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task WriteResultAsync(string path, HiddenChromeProbeResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed class HiddenChromeProbeResult
    {
        public int RequestedSeconds { get; init; }
        public double ElapsedSeconds { get; init; }
        public int ChromeProcessId { get; init; }
        public bool HideChanged { get; init; }
        public bool ShowChanged { get; init; }
        public int SuccessfulPolls { get; init; }
        public int MatchingPolls { get; init; }
        public int FailedPolls { get; init; }
        public string LastAssistantText { get; init; } = string.Empty;
        public int DebuggingPort { get; init; }
    }
}
