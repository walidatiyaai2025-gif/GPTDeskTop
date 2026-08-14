using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Data;

public sealed class ProjectStateStore
{
    public const int CurrentStateVersion = ProjectStateMigration.CurrentVersion;
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ProjectStateStore(string? rootDirectory = null)
    {
        var usingDefaultRoot = string.IsNullOrWhiteSpace(rootDirectory);
        _rootDirectory = usingDefaultRoot ? ResolveDefaultRoot() : Path.GetFullPath(rootDirectory!);
        Directory.CreateDirectory(_rootDirectory);
        if (usingDefaultRoot) MigrateLegacyDefaultRoot();
    }

    public async Task SaveAsync(ProjectState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.ProjectId)) throw new ArgumentException("ProjectId is required.", nameof(state));

        state.StateVersion = CurrentStateVersion;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        if (!ProjectStateValidator.IsValid(state)) throw new InvalidOperationException("Project state is invalid and cannot be persisted.");

        Directory.CreateDirectory(_rootDirectory);
        var path = GetPath(state.ProjectId);
        var backupPath = ProjectStateBackupPolicy.BackupPath(path);
        var temporaryPath = path + ".tmp";

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(path)) File.Copy(path, backupPath, true);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch { /* best-effort cleanup; primary/backup remain authoritative */ }
            }
        }
    }

    public async Task<ProjectState?> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(projectId);
        var backupPath = ProjectStateBackupPolicy.BackupPath(path);

        var primary = await TryLoadFileAsync(path, cancellationToken);
        if (primary is not null) return primary;

        var backup = await TryLoadFileAsync(backupPath, cancellationToken);
        if (backup is null) return null;

        // Self-heal a missing/corrupt primary from the last validated durable backup.
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            File.Copy(backupPath, path, true);
        }
        catch
        {
            // Recovery still succeeds from backup even when self-heal cannot write (for example read-only media).
        }

        return backup;
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

    public static string ResolveDefaultRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local)) return Path.Combine(local, "GPTDeskTop", "project-state");
        return Path.Combine(AppContext.BaseDirectory, "project-state");
    }

    private async Task<ProjectState?> TryLoadFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var state = await JsonSerializer.DeserializeAsync<ProjectState>(stream, _jsonOptions, cancellationToken);
            if (state is null) return null;
            state = ProjectStateMigration.Upgrade(state);
            return ProjectStateValidator.IsValid(state) ? state : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void MigrateLegacyDefaultRoot()
    {
        var legacyRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "project-state"));
        if (string.Equals(legacyRoot.TrimEnd(Path.DirectorySeparatorChar), _rootDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(legacyRoot)) return;

        foreach (var source in Directory.EnumerateFiles(legacyRoot, "*.json*", SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(_rootDirectory, Path.GetFileName(source));
            if (File.Exists(destination)) continue;
            try { File.Copy(source, destination, false); }
            catch { /* legacy migration is best-effort and never blocks application startup */ }
        }
    }

    private string GetPath(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("ProjectId is required.", nameof(projectId));
        var safeName = string.Concat(projectId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        return Path.Combine(_rootDirectory, safeName + ".json");
    }
}
