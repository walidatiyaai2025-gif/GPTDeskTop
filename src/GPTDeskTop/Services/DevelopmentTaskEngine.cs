using GPTDeskTop.Configuration;
using System.Text.Json;

namespace GPTDeskTop.Services;

public sealed class DevelopmentTaskEngine : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly string _statePath;
    private readonly string _catalogPath;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public DevelopmentTaskEngine(AppConfig config)
    {
        _config = config;
        _statePath = Path.Combine(AppContext.BaseDirectory, "development-task-state.json");
        _catalogPath = Path.Combine(AppContext.BaseDirectory, config.TaskAutomation.MessageCatalogFile);
    }

    public DevelopmentTaskState State { get; private set; } = new();
    public IReadOnlyList<string> Messages => LoadMessages();

    public void Start()
    {
        if (!_config.TaskAutomation.Enabled || _worker is not null)
            return;

        LoadState();
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
            return;

        await _cts.CancelAsync();
        if (_worker is not null)
        {
            try { await _worker; } catch (OperationCanceledException) { }
        }
        SaveState();
        _cts.Dispose();
        _cts = null;
        _worker = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        var work = TimeSpan.FromMinutes(Math.Clamp(_config.TaskAutomation.WorkWindowMinutes, 1, 120));
        var cooling = TimeSpan.FromMinutes(Math.Clamp(_config.TaskAutomation.CoolingWindowMinutes, 1, 120));
        var maxMessages = Math.Clamp(_config.TaskAutomation.MaxMessagesPerWindow, 1, 100);

        while (!token.IsCancellationRequested)
        {
            State.Status = DevelopmentTaskStatus.Working;
            State.WindowStartedUtc = DateTimeOffset.UtcNow;
            State.WindowMessageCount = 0;
            SaveState();

            var deadline = DateTimeOffset.UtcNow + work;
            while (DateTimeOffset.UtcNow < deadline && State.WindowMessageCount < maxMessages && !token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            if (token.IsCancellationRequested)
                break;

            State.Status = DevelopmentTaskStatus.Cooling;
            State.CoolingStartedUtc = DateTimeOffset.UtcNow;
            State.LastCheckpointUtc = DateTimeOffset.UtcNow;
            SaveState();
            await Task.Delay(cooling, token);
        }
    }

    public string? GetNextMessage()
    {
        var messages = LoadMessages();
        if (messages.Count == 0)
            return null;

        var index = Math.Clamp(State.MessageIndex, 0, messages.Count - 1);
        return messages[index];
    }

    public void MarkMessageSent(string? task = null, string? chatId = null)
    {
        var messages = LoadMessages();
        if (messages.Count > 0)
            State.MessageIndex = (State.MessageIndex + 1) % messages.Count;

        State.WindowMessageCount++;
        State.CurrentTask = task ?? State.CurrentTask;
        State.CurrentChatId = chatId ?? State.CurrentChatId;
        State.LastCheckpointUtc = DateTimeOffset.UtcNow;
        SaveState();
    }

    private List<string> LoadMessages()
    {
        if (!File.Exists(_catalogPath))
            return [];

        try
        {
            var json = File.ReadAllText(_catalogPath);
            var catalog = JsonSerializer.Deserialize<TaskMessageCatalog>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return catalog?.Messages?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void LoadState()
    {
        if (!File.Exists(_statePath))
            return;

        try
        {
            State = JsonSerializer.Deserialize<DevelopmentTaskState>(File.ReadAllText(_statePath)) ?? new();
        }
        catch
        {
            State = new();
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persistence must never terminate the monitor process.
        }
    }
}

public sealed class TaskMessageCatalog
{
    public List<string> Messages { get; set; } = [];
}

public sealed class DevelopmentTaskState
{
    public DevelopmentTaskStatus Status { get; set; } = DevelopmentTaskStatus.Stopped;
    public int MessageIndex { get; set; }
    public int WindowMessageCount { get; set; }
    public DateTimeOffset? WindowStartedUtc { get; set; }
    public DateTimeOffset? CoolingStartedUtc { get; set; }
    public DateTimeOffset? LastCheckpointUtc { get; set; }
    public string? CurrentTask { get; set; }
    public string? CurrentChatId { get; set; }
}

public enum DevelopmentTaskStatus
{
    Stopped,
    Working,
    Cooling
}
