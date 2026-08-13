using System.Security.Cryptography;
using System.Text;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class ProjectTaskService
{
    private readonly ProjectStateStore _store;
    public ProjectTaskService(ProjectStateStore store) => _store = store;

    public async Task<ProjectTaskState> AddAsync(string projectId, string taskId, string title, string priority = "Normal", CancellationToken ct = default)
    {
        var state = await RequireProjectAsync(projectId, ct);
        if (state.Tasks.Any(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Task '{taskId}' already exists.");

        var fingerprint = CreateFingerprint(projectId, title);
        if (state.Tasks.Any(t => t.VerificationEvidence.Contains("fingerprint:" + fingerprint, StringComparer.Ordinal)))
            throw new InvalidOperationException("An equivalent task already exists for this project.");

        var task = new ProjectTaskState { TaskId = taskId.Trim(), Title = title.Trim(), Priority = priority.Trim(), Status = ProjectTaskStatus.Ready };
        task.VerificationEvidence.Add("fingerprint:" + fingerprint);
        state.Tasks.Add(task);
        await _store.SaveAsync(state, ct);
        return task;
    }

    public async Task SetStatusAsync(string projectId, string taskId, ProjectTaskStatus status, string? blockedReason = null, CancellationToken ct = default)
    {
        var state = await RequireProjectAsync(projectId, ct);
        var task = state.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException(taskId);
        task.Status = status;
        if (status == ProjectTaskStatus.InProgress && task.StartedAt is null) task.StartedAt = DateTimeOffset.UtcNow;
        if (status == ProjectTaskStatus.Completed) task.CompletedAt = DateTimeOffset.UtcNow;
        task.BlockedReason = status == ProjectTaskStatus.Blocked ? blockedReason?.Trim() ?? "Blocked" : string.Empty;
        await _store.SaveAsync(state, ct);
    }

    private static string CreateFingerprint(string projectId, string title)
    {
        var normalized = $"{projectId.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private async Task<ProjectState> RequireProjectAsync(string projectId, CancellationToken ct) =>
        await _store.LoadAsync(projectId, ct) ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
}
