using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Data;

public sealed class ProjectStateStore
{
    public const int CurrentStateVersion = 1;
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ProjectStateStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "project-state");
    }

    public async Task SaveAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.ProjectId)) throw new ArgumentException("ProjectId is required.", nameof(state));
        state.StateVersion = CurrentStateVersion;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(_rootDirectory);
        var path = GetPath(state.ProjectId);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    public async Task<ProjectState?> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(projectId);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var state = await JsonSerializer.DeserializeAsync<ProjectState>(stream, _jsonOptions, cancellationToken);
        if (state is null) return null;
        if (state.StateVersion > CurrentStateVersion)
            throw new InvalidOperationException($"Project state version {state.StateVersion} is newer than supported version {CurrentStateVersion}.");
        return Migrate(state);
    }

    public IReadOnlyList<string> ListProjectIds()
    {
        if (!Directory.Exists(_rootDirectory)) return [];
        return Directory.EnumerateFiles(_rootDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProjectState Migrate(ProjectState state)
    {
        if (state.StateVersion <= 0) state.StateVersion = 1;
        return state;
    }

    private string GetPath(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("ProjectId is required.", nameof(projectId));
        var safeName = string.Concat(projectId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        return Path.Combine(_rootDirectory, safeName + ".json");
    }
}
