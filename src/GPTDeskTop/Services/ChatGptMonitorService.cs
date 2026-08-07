using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ChatGptMonitorService
{
    private readonly ChromeDevToolsService _chrome;
    private readonly LocalDatabase _database;
    private readonly MonitoringConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public event Action<string>? Activity;
    public event Action? HistoryChanged;

    public bool IsRunning => _worker is { IsCompleted: false };

    public ChatGptMonitorService(ChromeDevToolsService chrome, LocalDatabase database, MonitoringConfig config)
    {
        _chrome = chrome;
        _database = database;
        _config = config;
    }

    public Task StartAsync(ChromeTab tab, string autoReply)
    {
        if (IsRunning)
            throw new InvalidOperationException("Monitoring is already running.");
        if (string.IsNullOrWhiteSpace(autoReply))
            throw new ArgumentException("Auto reply text cannot be empty.", nameof(autoReply));

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => MonitorLoopAsync(tab, autoReply.Trim(), _cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        try
        {
            if (_worker is not null)
                await _worker;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _worker = null;
        }
    }

    private async Task MonitorLoopAsync(ChromeTab tab, string autoReply, CancellationToken cancellationToken)
    {
        Activity?.Invoke($"Monitoring tab: {tab.Title} [{tab.Id}]");
        Activity?.Invoke($"Auto reply: {autoReply}");

        var initial = await _chrome.GetChatStateAsync(tab, cancellationToken);
        var lastHandledText = initial.LastAssistantText;
        var candidateText = string.Empty;
        var candidateSince = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(300, _config.PollIntervalMilliseconds)));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var state = await _chrome.GetChatStateAsync(tab, cancellationToken);
                var text = state.LastAssistantText.Trim();

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
                    Activity?.Invoke("New ChatGPT response detected; waiting until it is stable...");
                    continue;
                }

                if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds)
                    continue;

                lastHandledText = text;
                candidateText = string.Empty;
                candidateSince = DateTimeOffset.MinValue;

                Activity?.Invoke($"ChatGPT replied: {Shorten(text, 220)}");
                await _database.AddLogAsync("Inbound", string.Empty, text, "Detected", cancellationToken);
                HistoryChanged?.Invoke();

                var sent = await _chrome.SendChatMessageAsync(tab, autoReply, cancellationToken);
                if (sent)
                {
                    Activity?.Invoke($"Auto reply sent: {autoReply}");
                    await _database.AddLogAsync("Outbound", autoReply, string.Empty, "Sent", cancellationToken);
                }
                else
                {
                    Activity?.Invoke("Auto reply could not be sent: prompt editor/send button was not ready.");
                    await _database.AddLogAsync("Outbound", autoReply, string.Empty, "Failed", cancellationToken);
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
                Activity?.Invoke($"Monitor error: {ex.Message}");
                await Task.Delay(1500, cancellationToken);
            }
        }
    }

    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
