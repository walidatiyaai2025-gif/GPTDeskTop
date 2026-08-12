using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class LastWorkingStatePersistenceTests
{
    [Fact]
    public async Task DesiredMonitorIdsArePersistedSortedDistinctAndCanBeCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var database = new LocalDatabase(Path.Combine(root, "state.db"));
            await database.InitializeAsync();

            await LastWorkingStateService.ReplaceDesiredMonitorIdsAsync(database, [7, 2, 7, -1, 0]);
            Assert.Equal([2L, 7L], await LastWorkingStateService.GetDesiredMonitorIdsAsync(database));

            await LastWorkingStateService.SetMonitorDesiredRunningAsync(database, 4, true);
            await LastWorkingStateService.SetMonitorDesiredRunningAsync(database, 2, false);
            Assert.Equal([4L, 7L], await LastWorkingStateService.GetDesiredMonitorIdsAsync(database));

            await LastWorkingStateService.ClearDesiredMonitorsAsync(database);
            Assert.Empty(await LastWorkingStateService.GetDesiredMonitorIdsAsync(database));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void MainFormTracksExplicitStartStopIntentWithoutClearingItDuringShutdownCleanup()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "MainForm.cs"));

        Assert.Contains("SetMonitorDesiredRunningAsync(_database, monitor.Id, true)", source, StringComparison.Ordinal);
        Assert.Contains("SetMonitorDesiredRunningAsync(_database, monitorId, false)", source, StringComparison.Ordinal);
        Assert.Contains("ClearDesiredMonitorsAsync(_database)", source, StringComparison.Ordinal);

        var shutdownStart = source.IndexOf("private async Task CompleteShutdownAsync()", StringComparison.Ordinal);
        Assert.True(shutdownStart >= 0);
        var shutdownSource = source[shutdownStart..];
        Assert.Contains("await _monitor.StopAllAsync().WaitAsync(timeout.Token);", shutdownSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearDesiredMonitorsAsync", shutdownSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramResumesPersistedMonitorsAndOnlyActiveDevelopmentTaskState()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Program.cs"));

        Assert.Contains("LastWorkingStateService.ResumeDesiredMonitorsAsync", source, StringComparison.Ordinal);
        Assert.Contains("LastWorkingStateService.ReplaceDesiredMonitorIdsAsync", source, StringComparison.Ordinal);
        Assert.Contains("developmentRuntime.ResumeIfActiveAsync()", source, StringComparison.Ordinal);
        Assert.Contains("Runtime.DevelopmentTaskAutoResumed", source, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
