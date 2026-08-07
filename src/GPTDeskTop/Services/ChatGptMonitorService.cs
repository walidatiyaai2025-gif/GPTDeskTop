using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChatGptMonitorService
{
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly MonitoringConfig _config;
    private readonly object _sync = new();
    private readonly Dictionary<long, MonitorRuntime> _running = new();

    public event Action<long, string>? Activity;
    public event Action? HistoryChanged;
    public event Action? RunningStateChanged;
    public event Action<long, string, string, bool>? ResponseReceived;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _running.Count > 0;
        }
    }

    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;
    }

    public bool IsMonitorRunning(long monitorId)
    {
        lock (_sync)
            return _running.ContainsKey(monitorId);
    }

    public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
    {
        if (monitor.Id <= 0)
            throw new InvalidOperationException("Save the monitor before starting it.");
        if (string.IsNullOrWhiteSpace(monitor.AutoReply))
            throw new InvalidOperationException("Auto reply text cannot be empty.");

        lock (_sync)
        {
            if (_running.ContainsKey(monitor.Id))
                return Task.CompletedTask;

            var cts = new CancellationTokenSource();
            var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token));
            _running.Add(monitor.Id, new MonitorRuntime(cts, worker));
        }

        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}");
        RunningStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task StopMonitorAsync(long monitorId)
    {
        MonitorRuntime? runtime;
        lock (_sync)
        {
            if (!_running.Remove(monitorId, out runtime))
                return;
        }

        runtime.Cancellation.Cancel();
        try
        {
            await runtime.Worker;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            runtime.Cancellation.Dispose();
            Activity?.Invoke(monitorId, "Stopped.");
            RunningStateChanged?.Invoke();
        }
    }

    public async Task StopAllAsync()
    {
        long[] ids;
        lock (_sync)
            ids = _running.Keys.ToArray();

        await Task.WhenAll(ids.Select(StopMonitorAsync));
    }

    private async Task MonitorLoopAsync(SavedMonitor monitor, ChromeTab tab, CancellationToken cancellationToken)
    {
        var prefix = $"[{monitor.Title}]";
        var timerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        var replyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);

        Activity?.Invoke(monitor.Id, $"{prefix} Monitoring Tab ID {tab.Id}");
        Activity?.Invoke(monitor.Id, $"{prefix} Auto reply: {monitor.AutoReply}");
        Activity?.Invoke(monitor.Id, $"{prefix} Timer: {timerSeconds}s | Reply delay: {replyDelaySeconds}s");

        try
        {
            var initial = await _chrome.GetChatStateAsync(tab, cancellationToken);
            var lastHandledText = GetEffectiveResponse(initial);
            var candidateText = string.Empty;
            var candidateSince = DateTimeOffset.MinValue;

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(timerSeconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                    var text = GetEffectiveResponse(state);

                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || string.Equals(text, lastHandledText, StringComparison.Ordinal))
                    {
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        continue;
                    }

                    if (!string.Equals(candidateText, text, StringComparison.Ordinal))
                    {
                        candidateText = text;
                        candidateSince = DateTimeOffset.UtcNow;
                        Activity?.Invoke(monitor.Id, $"{prefix} New response detected; waiting for stability...");
                        continue;
                    }

                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds)
                        continue;

                    lastHandledText = text;
                    candidateText = string.Empty;
                    candidateSince = DateTimeOffset.MinValue;

                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText) || IsErrorResponse(text);
                    var status = isError ? "Error" : "Detected";

                    Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT replied{(isError ? " with ERROR" : string.Empty)}: {Shorten(text, 220)}");
                    await _database.AddLogAsync(
                        "Inbound", string.Empty, text, status, monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke();
                    ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);

                    if (isError)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Error response saved. Refreshing this tab...");
                        try
                        {
                            await _chrome.ReloadTabAsync(tab, cancellationToken);
                            await _database.AddLogAsync(
                                "System", "Page.reload", text, "RefreshedAfterError", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            Activity?.Invoke(monitor.Id, $"{prefix} Tab refreshed after ChatGPT error.");
                            await Task.Delay(Math.Max(1500, _config.DelayAfterSendMilliseconds), cancellationToken);
                        }
                        catch (Exception refreshEx) when (refreshEx is not OperationCanceledException)
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} Refresh failed: {refreshEx.Message}");
                            await _database.AddLogAsync(
                                "System", "Page.reload", refreshEx.Message, "RefreshFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                        }
                        continue;
                    }

                    if (replyDelaySeconds > 0)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds} second(s) before sending auto reply...");
                        await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken);

                        var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken);
                        var latestText = GetEffectiveResponse(recheck);
                        if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal))
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} Auto reply cancelled because page state changed during send delay.");
                            await _database.AddLogAsync(
                                "System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            continue;
                        }
                    }

                    var sent = await _chrome.SendChatMessageAsync(tab, monitor.AutoReply, cancellationToken);
                    if (sent)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Auto reply sent: {monitor.AutoReply}");
                        await _database.AddLogAsync(
                            "Outbound", monitor.AutoReply, string.Empty, "Sent", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    }
                    else
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Send failed: editor/send button not ready.");
                        await _database.AddLogAsync(
                            "Outbound", monitor.AutoReply, string.Empty, "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    }

                    HistoryChanged?.Invoke();
                    await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Activity?.Invoke(monitor.Id, $"{prefix} Monitor error: {ex.Message}");
                    await Task.Delay(1500, cancellationToken);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_running.TryGetValue(monitor.Id, out var current) && current.Cancellation.Token == cancellationToken)
                    _running.Remove(monitor.Id);
            }
            RunningStateChanged?.Invoke();
        }
    }

    private static string GetEffectiveResponse(ChatPageState state)
        => !string.IsNullOrWhiteSpace(state.ErrorText)
            ? state.ErrorText.Trim()
            : state.LastAssistantText.Trim();

    private static bool IsErrorResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        string[] markers =
        {
            "something went wrong",
            "there was an error",
            "network error",
            "failed to generate",
            "error generating",
            "unable to generate",
            "unable to load",
            "حدث خطأ",
            "خطأ في الشبكة",
            "تعذر إنشاء",
            "تعذر تحميل"
        };
        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private sealed record MonitorRuntime(CancellationTokenSource Cancellation, Task Worker);
}
