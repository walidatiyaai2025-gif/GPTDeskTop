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

    public bool IsRunning { get { lock (_sync) return _running.Count > 0; } }

    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)
    {
        _chrome = chrome; _database = database; _config = config;
    }

    public bool IsMonitorRunning(long monitorId) { lock (_sync) return _running.ContainsKey(monitorId); }

    public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
    {
        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before starting it.");
        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");
        lock (_sync)
        {
            if (_running.ContainsKey(monitor.Id)) return Task.CompletedTask;
            var cts = new CancellationTokenSource();
            var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token));
            _running.Add(monitor.Id, new MonitorRuntime(cts, worker));
        }
        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}"); RunningStateChanged?.Invoke(); return Task.CompletedTask;
    }

    public async Task StopMonitorAsync(long monitorId)
    {
        MonitorRuntime? runtime;
        lock (_sync) { if (!_running.Remove(monitorId, out runtime)) return; }
        runtime.Cancellation.Cancel();
        try { await runtime.Worker; } catch (OperationCanceledException) { }
        finally { runtime.Cancellation.Dispose(); Activity?.Invoke(monitorId, "Stopped."); RunningStateChanged?.Invoke(); }
    }

    public async Task StopAllAsync()
    {
        long[] ids; lock (_sync) ids = _running.Keys.ToArray();
        await Task.WhenAll(ids.Select(StopMonitorAsync));
    }

    private async Task MonitorLoopAsync(SavedMonitor monitor, ChromeTab tab, CancellationToken cancellationToken)
    {
        var timerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        var replyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Timer {timerSeconds}s | Delay {replyDelaySeconds}s | Reply: {monitor.AutoReply}");

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
                    var prefix = $"[{monitor.Title}]";
                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                    var text = GetEffectiveResponse(state);
                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || string.Equals(text, lastHandledText, StringComparison.Ordinal))
                    { candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; continue; }

                    if (!string.Equals(candidateText, text, StringComparison.Ordinal))
                    { candidateText = text; candidateSince = DateTimeOffset.UtcNow; Activity?.Invoke(monitor.Id, $"{prefix} New response detected..."); continue; }
                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;

                    lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText) || IsErrorResponse(text);
                    await _database.AddLogAsync("Inbound", string.Empty, text, isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke(); ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);

                    if (isError && IsDeliveryTimeout(text))
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat...");
                        var recoveryMessage = await _database.GetSettingAsync("TimeoutRecoveryMessage", cancellationToken) ?? "كمل";
                        var oldTab = tab;
                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                        var sent = await _chrome.SendChatMessageAsync(newTab, recoveryMessage, cancellationToken);
                        if (!sent) throw new InvalidOperationException("New chat opened but the recovery message could not be sent.");

                        monitor.TabId = newTab.Id;
                        monitor.Title = string.IsNullOrWhiteSpace(newTab.Title) ? "Recovered ChatGPT Chat" : newTab.Title;
                        monitor.Url = newTab.Url;
                        await _database.SaveMonitorAsync(monitor, cancellationToken);
                        await _database.AddLogAsync("System", recoveryMessage, text, "RecoveredToNewChat", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        await _database.AddLogAsync("Outbound", recoveryMessage, string.Empty, "RecoverySent", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                        HistoryChanged?.Invoke();

                        tab = newTab;
                        lastHandledText = string.Empty;
                        candidateText = string.Empty;
                        candidateSince = DateTimeOffset.MinValue;
                        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}.");
                        await _chrome.CloseTabAsync(oldTab, cancellationToken);
                        continue;
                    }

                    if (isError)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Error saved. Refreshing only this tab...");
                        try
                        {
                            await _chrome.ReloadTabAsync(tab, cancellationToken);
                            await _database.AddLogAsync("System", "Page.reload", text, "RefreshedAfterError", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            await Task.Delay(Math.Max(1500, _config.DelayAfterSendMilliseconds), cancellationToken);
                        }
                        catch (Exception refreshEx) when (refreshEx is not OperationCanceledException)
                        {
                            await _database.AddLogAsync("System", "Page.reload", refreshEx.Message, "RefreshFailed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                        }
                        continue;
                    }

                    if (replyDelaySeconds > 0)
                    {
                        Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before auto reply...");
                        await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken);
                        var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken);
                        var latestText = GetEffectiveResponse(recheck);
                        if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal))
                        {
                            await _database.AddLogAsync("System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke(); continue;
                        }
                    }

                    var autoSent = await _chrome.SendChatMessageAsync(tab, monitor.AutoReply, cancellationToken);
                    await _database.AddLogAsync("Outbound", monitor.AutoReply, string.Empty, autoSent ? "Sent" : "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke();
                    await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Activity?.Invoke(monitor.Id, $"Monitor error: {ex.Message}");
                    await _database.AddLogAsync("System", string.Empty, ex.Message, "MonitorException", monitor.Id, tab.Id, monitor.Title, cancellationToken);
                    HistoryChanged?.Invoke();
                    await Task.Delay(1500, cancellationToken);
                }
            }
        }
        finally
        {
            lock (_sync)
                if (_running.TryGetValue(monitor.Id, out var current) && current.Cancellation.Token == cancellationToken) _running.Remove(monitor.Id);
            RunningStateChanged?.Invoke();
        }
    }

    private static string GetEffectiveResponse(ChatPageState state)
        => !string.IsNullOrWhiteSpace(state.ErrorText) ? state.ErrorText.Trim() : state.LastAssistantText.Trim();

    private static bool IsDeliveryTimeout(string text)
        => text.Contains("message delivery timed out", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] markers = { "message delivery timed out", "something went wrong", "there was an error", "network error", "failed to generate", "error generating", "unable to generate", "unable to load", "حدث خطأ", "خطأ في الشبكة", "تعذر إنشاء", "تعذر تحميل" };
        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record MonitorRuntime(CancellationTokenSource Cancellation, Task Worker);
}
