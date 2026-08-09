namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationImportRuntimeBoundaryTests
{
    [Fact]
    public void SettingsImportChecksRunningMonitorBoundaryBeforeFileFlowAndBeforeApply()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var start = source.IndexOf("private async Task ImportConfigurationBackupAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private bool EnsureConfigurationImportRuntimeSafe()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var import = source[start..end];

        Assert.Equal(2, Count(import, "EnsureConfigurationImportRuntimeSafe()"));
        var firstGuard = import.IndexOf("EnsureConfigurationImportRuntimeSafe()", StringComparison.Ordinal);
        var dialog = import.IndexOf("new OpenFileDialog", StringComparison.Ordinal);
        var secondGuard = import.LastIndexOf("EnsureConfigurationImportRuntimeSafe()", StringComparison.Ordinal);
        var apply = import.IndexOf("await service.ApplyAsync(plan)", StringComparison.Ordinal);
        Assert.True(firstGuard >= 0 && firstGuard < dialog);
        Assert.True(secondGuard > dialog && secondGuard < apply);
    }

    [Fact]
    public void SettingsRuntimeGuardUsesPredicateAndNeverCouplesDialogToMonitorService()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("private readonly Func<bool> _hasRunningMonitors", source, StringComparison.Ordinal);
        Assert.Contains("Func<bool>? hasRunningMonitors = null", source, StringComparison.Ordinal);
        Assert.Contains("_hasRunningMonitors = hasRunningMonitors ?? (() => false)", source, StringComparison.Ordinal);
        Assert.Contains("Use Stop All in the main window", source, StringComparison.Ordinal);
        Assert.Contains("Running monitors are never stopped automatically", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopAllAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormProvidesLiveRuntimePredicateAndRefreshesMonitorPresentationAfterSettings()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var start = source.IndexOf("private async Task OpenSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LaunchChromeAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var openSettings = source[start..end];

        Assert.Contains("new SettingsForm(_database, () => _monitor.IsRunning)", openSettings, StringComparison.Ordinal);
        Assert.Contains("await RefreshMonitorsAsync()", openSettings, StringComparison.Ordinal);
        Assert.Contains("SelectCurrentMonitor()", openSettings, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"DefaultAutoReply\")", openSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalSettingsSaveAndBackupExportRemainAvailableWhileMonitorsRun()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var saveStart = source.IndexOf("private async Task SaveSettingsAsync()", StringComparison.Ordinal);
        var exportStart = source.IndexOf("private async Task ExportConfigurationBackupAsync()", saveStart, StringComparison.Ordinal);
        var importStart = source.IndexOf("private async Task ImportConfigurationBackupAsync()", exportStart, StringComparison.Ordinal);
        Assert.True(saveStart >= 0 && exportStart > saveStart && importStart > exportStart);

        var save = source[saveStart..exportStart];
        var export = source[exportStart..importStart];
        Assert.DoesNotContain("EnsureConfigurationImportRuntimeSafe", save, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureConfigurationImportRuntimeSafe", export, StringComparison.Ordinal);
        Assert.Contains("await _database.SetSettingsAsync(desiredSettings)", save, StringComparison.Ordinal);
        Assert.Contains("await service.ExportAsync(dialog.FileName)", export, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while (true)
        {
            var index = source.IndexOf(value, offset, StringComparison.Ordinal);
            if (index < 0) return count;
            count++;
            offset = index + value.Length;
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
