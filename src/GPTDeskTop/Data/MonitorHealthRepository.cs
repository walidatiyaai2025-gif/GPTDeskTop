using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Data;

/// <summary>
/// Persistence abstraction for monitor heartbeat, recovery state, and durable
/// orchestrator project snapshots. Project snapshots use the existing local
/// settings store so upgrades do not introduce a second persistence engine.
/// </summary>
public sealed class MonitorHealthRepository
{
    public const int CurrentProjectStateVersion = 1;
    private static readonly JsonSerializerOptions ProjectJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabase _database;

    public MonitorHealthRepository(LocalDatabase database)
    {
        _database = database;
    }

    public Task UpdateHeartbeatAsync(
        int monitorId,
        string? tabId,
        CancellationToken cancellationToken = default)
    {
        return _database.SetSettingAsync(
            $"MonitorHealth:{monitorId}:Heartbeat",
            $"{DateTimeOffset.UtcNow:O}|{tabId}",
            cancellationToken);
    }

    public Task SetStatusAsync(
        int monitorId,
        string status,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        return _database.SetSettingAsync(
            $"MonitorHealth:{monitorId}:Status",
            $"{status}|{error}",
            cancellationToken);
    }

    public Task SaveProjectStateAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.ProjectId))
            throw new ArgumentException("ProjectId is required.", nameof(state));

        state.StateVersion = CurrentProjectStateVersion;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(state, ProjectJsonOptions);
        return _database.SetSettingAsync(ProjectStateKey(state.ProjectId), json, cancellationToken);
    }

    public async Task<ProjectState?> LoadProjectStateAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("ProjectId is required.", nameof(projectId));

        var json = await _database.GetSettingAsync(ProjectStateKey(projectId), cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;

        var state = JsonSerializer.Deserialize<ProjectState>(json, ProjectJsonOptions);
        if (state is null) return null;
        if (state.StateVersion <= 0) state.StateVersion = 1;
        if (state.StateVersion > CurrentProjectStateVersion)
            throw new InvalidOperationException($"Project state version {state.StateVersion} is newer than supported version {CurrentProjectStateVersion}.");
        return state;
    }

    private static string ProjectStateKey(string projectId)
    {
        var normalized = projectId.Trim().ToLowerInvariant();
        return $"Orchestrator:ProjectState:{normalized}";
    }
}
