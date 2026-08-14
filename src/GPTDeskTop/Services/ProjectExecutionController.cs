using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ProjectExecutionResult(bool Success, string Message);

public sealed class ProjectExecutionController
{
    private readonly ProjectStateStore _store;
    private readonly LocalDatabase _database;
    private readonly ChatGptMonitorService _monitorService;
    private readonly ChromeDevToolsService _chrome;

    public ProjectExecutionController(ProjectStateStore store, LocalDatabase database, ChatGptMonitorService monitorService, ChromeDevToolsService chrome)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
    }

    public async Task<IReadOnlyList<SavedMonitor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => await _database.GetSavedMonitorsAsync(cancellationToken);

    public async Task<ProjectState> InitializeAsync(string repoUrl, string? mainGoal, string? branch, CancellationToken cancellationToken = default)
    {
        var registry = new ProjectRegistry(_store);
        var state = await registry.GetOrCreateAsync(repoUrl, mainGoal, cancellationToken);
        if (!string.IsNullOrWhiteSpace(branch)) state.CurrentBranch = branch.Trim();
        state.Status = "IDLE";
        state.NextAction = "Select a saved monitor and choose Start / Continue Project.";
        await _store.SaveAsync(state, cancellationToken);
        return state;
    }

    public async Task<ProjectExecutionResult> UpdateProjectAsync(ProjectState state, string? mainGoal, string? branch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!string.IsNullOrWhiteSpace(mainGoal)) state.MainGoal = mainGoal.Trim();
        if (!string.IsNullOrWhiteSpace(branch)) state.CurrentBranch = branch.Trim();
        state.NextAction = "Project settings updated. Choose Start / Continue when ready.";
        await _store.SaveAsync(state, cancellationToken);
        return new(true, "Project settings saved.");
    }

    public async Task<ProjectExecutionResult> ArchiveAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CurrentMonitorId > 0 && _monitorService.IsMonitorRunning(state.CurrentMonitorId)) await _monitorService.StopMonitorAsync(state.CurrentMonitorId);
        state.Status = "ARCHIVED";
        state.NextAction = "Archived by operator.";
        state.CurrentMonitorId = 0;
        await _store.SaveAsync(state, cancellationToken);
        return new(true, "Project archived and its running monitor stopped.");
    }

    public async Task<string?> GetConversationUrlAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
        var monitor = state.CurrentMonitorId > 0 ? monitors.FirstOrDefault(x => x.Id == state.CurrentMonitorId) : null;
        monitor ??= monitors.FirstOrDefault(x => !string.IsNullOrWhiteSpace(state.CurrentChatId) && ChatGptConversationIdentity.IsSame(x.Url, state.CurrentChatId));
        if (!string.IsNullOrWhiteSpace(monitor?.Url)) return monitor.Url;
        if (Uri.TryCreate(state.CurrentChatId, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "www.chatgpt.com", StringComparison.OrdinalIgnoreCase))) return state.CurrentChatId;
        return null;
    }

    public async Task<ProjectExecutionResult> StartOrContinueAsync(ProjectState state, long monitorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Tasks.Any(t => t.Status == ProjectTaskStatus.AwaitingApproval) || IsStatus(state.Status, "WAITING_FOR_HUMAN", "HUMAN_REQUIRED", "AWAITING_APPROVAL")) return new(false, "Project requires human action before automation can continue.");
        if (IsAutomationOwnedStatus(state.Status)) return new(true, "Project automation already owns this work cycle. No duplicate message was sent.");
        var monitor = (await _database.GetSavedMonitorsAsync(cancellationToken)).FirstOrDefault(x => x.Id == monitorId);
        if (monitor is null) return new(false, "The selected saved monitor no longer exists.");
        if (!monitor.Enabled) return new(false, "The selected monitor is disabled. Enable it before starting the project.");
        var tabs = await _chrome.GetTabsAsync(cancellationToken);
        var tab = tabs.FirstOrDefault(x => string.Equals(x.Id, monitor.TabId, StringComparison.Ordinal) && ChatGptConversationIdentity.IsSame(x.Url, monitor.Url)) ?? tabs.FirstOrDefault(x => ChatGptConversationIdentity.IsSame(x.Url, monitor.Url));
        if (tab is null) return new(false, "The monitor conversation is not open in the managed Chrome session. Open/recover it, then retry.");
        state.Status = "ACTIVE";
        state.CurrentMonitorId = monitor.Id;
        state.CurrentChatId = monitor.Url;
        state.NextAction = "Starting the selected monitor and preparing a verified continuation message.";
        await _store.SaveAsync(state, cancellationToken);
        if (!_monitorService.IsMonitorRunning(monitor.Id)) await _monitorService.StartMonitorAsync(monitor, tab);
        if (!_monitorService.IsMonitorRunning(monitor.Id))
        {
            state.Status = "BLOCKED";
            state.NextAction = "The monitor could not be started. Check the monitor activity log for the exact reason.";
            await _store.SaveAsync(state, cancellationToken);
            return new(false, state.NextAction);
        }
        var message = string.IsNullOrWhiteSpace(monitor.AutoReply) ? "كمل" : monitor.AutoReply.Trim();
        var sent = await _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken);
        if (!sent)
        {
            await _monitorService.StopMonitorAsync(monitor.Id);
            state.Status = "BLOCKED";
            state.NextAction = "Initial continuation message was not verified. Inspect the ChatGPT tab and retry.";
            await _store.SaveAsync(state, cancellationToken);
            return new(false, state.NextAction);
        }
        state.Status = "GENERATING";
        state.NextAction = "Monitor is running. Waiting for ChatGPT response and GitHub progress evidence.";
        state.LastVerifiedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(state, cancellationToken);
        return new(true, $"Started monitor #{monitor.Id} and verified the initial continuation message.");
    }

    public async Task<ProjectExecutionResult> PauseAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CurrentMonitorId > 0 && _monitorService.IsMonitorRunning(state.CurrentMonitorId)) await _monitorService.StopMonitorAsync(state.CurrentMonitorId);
        state.Status = "IDLE";
        state.NextAction = "Paused by operator. Choose Start / Continue Project to resume.";
        await _store.SaveAsync(state, cancellationToken);
        return new(true, "Project paused. No new monitor messages will be sent until resumed.");
    }

    public async Task<ProjectExecutionResult> StopAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CurrentMonitorId > 0 && _monitorService.IsMonitorRunning(state.CurrentMonitorId)) await _monitorService.StopMonitorAsync(state.CurrentMonitorId);
        state.Status = "IDLE";
        state.NextAction = "Stopped by operator. Choose Start / Continue Project when ready to resume.";
        state.CurrentMonitorId = 0;
        state.CurrentChatId = string.Empty;
        await _store.SaveAsync(state, cancellationToken);
        return new(true, "Project automation stopped and returned to IDLE.");
    }

    public async Task<ProjectExecutionResult> RetryAsync(ProjectState state, long monitorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Tasks.Any(t => t.Status == ProjectTaskStatus.AwaitingApproval) || IsStatus(state.Status, "WAITING_FOR_HUMAN", "HUMAN_REQUIRED", "AWAITING_APPROVAL")) return new(false, "Human approval is still required; Retry is intentionally blocked.");
        state.Status = "RECOVERING";
        state.RetryCount++;
        state.NextAction = "Bounded operator retry requested.";
        await _store.SaveAsync(state, cancellationToken);
        state.Status = "IDLE";
        await _store.SaveAsync(state, cancellationToken);
        return await StartOrContinueAsync(state, monitorId, cancellationToken);
    }

    public static bool IsAutomationOwnedStatus(string? status) => IsStatus(status, "ACTIVE", "GENERATING", "WAITING_EXTERNAL", "MODEL_DELAYED_RESPONSE", "SUSPECTED_STALL", "VERIFYING", "RECOVERING", "ROTATING_CHAT", "RUNNING", "WAITING_FOR_REPLY");
    private static bool IsStatus(string? value, params string[] candidates) => candidates.Any(x => string.Equals(value?.Trim(), x, StringComparison.OrdinalIgnoreCase));
}
