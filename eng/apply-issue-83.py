from pathlib import Path

# SettingsForm: runtime predicate without monitor-service coupling + two import safety checks.
settings_path = Path('src/GPTDeskTop/UI/SettingsForm.cs')
settings = settings_path.read_text(encoding='utf-8')
field_anchor = '    private readonly LocalDatabase _database;\n'
field_add = '    private readonly Func<bool> _hasRunningMonitors;\n'
if field_add not in settings:
    if settings.count(field_anchor) != 1:
        raise RuntimeError(f'SettingsForm field anchor count={settings.count(field_anchor)}')
    settings = settings.replace(field_anchor, field_anchor + field_add, 1)

old_ctor = '''    public SettingsForm(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
'''
new_ctor = '''    public SettingsForm(LocalDatabase database, Func<bool>? hasRunningMonitors = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _hasRunningMonitors = hasRunningMonitors ?? (() => false);
'''
if settings.count(old_ctor) != 1:
    raise RuntimeError(f'SettingsForm constructor anchor count={settings.count(old_ctor)}')
settings = settings.replace(old_ctor, new_ctor, 1)

import_anchor = '''    private async Task ImportConfigurationBackupAsync()
    {
        if (_busy) return;

        using var dialog = new OpenFileDialog
'''
import_replacement = '''    private async Task ImportConfigurationBackupAsync()
    {
        if (_busy) return;
        if (!EnsureConfigurationImportRuntimeSafe()) return;

        using var dialog = new OpenFileDialog
'''
if settings.count(import_anchor) != 1:
    raise RuntimeError(f'Import initial guard anchor count={settings.count(import_anchor)}')
settings = settings.replace(import_anchor, import_replacement, 1)

apply_anchor = '''            SetBusy(true, "Importing configuration backup transactionally…");
            var result = await service.ApplyAsync(plan);
'''
apply_replacement = '''            if (!EnsureConfigurationImportRuntimeSafe()) return;

            SetBusy(true, "Importing configuration backup transactionally…");
            var result = await service.ApplyAsync(plan);
'''
if settings.count(apply_anchor) != 1:
    raise RuntimeError(f'Import pre-apply guard anchor count={settings.count(apply_anchor)}')
settings = settings.replace(apply_anchor, apply_replacement, 1)

method_insert = '''    private bool EnsureConfigurationImportRuntimeSafe()
    {
        if (!_hasRunningMonitors()) return true;

        _statusLabel.Text = "Configuration import is blocked while monitor runtime is active. Stop All monitors first.";
        MessageBox.Show(
            this,
            "Configuration import cannot run while one or more monitors are active. Use Stop All in the main window, then retry the import. Running monitors are never stopped automatically.",
            "Stop Monitors Before Import",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

'''
close_anchor = '\n}\n'
last = settings.rfind(close_anchor)
if last < 0:
    raise RuntimeError('SettingsForm closing brace anchor not found')
settings = settings[:last] + '\n' + method_insert + settings[last:]
settings_path.write_text(settings, encoding='utf-8')

# MainForm: provide runtime predicate and refresh persisted presentation after Settings returns OK.
main_path = Path('src/GPTDeskTop/UI/MainForm.cs')
main = main_path.read_text(encoding='utf-8')
old_open = '''    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        if (_selectedMonitor is null) _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        AppendActivity("Global monitoring, rotation and notification settings saved.");
    }
'''
new_open = '''    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database, () => _monitor.IsRunning);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        await RefreshMonitorsAsync();
        if (_selectedMonitor is null)
            _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        else
            SelectCurrentMonitor();
        AppendActivity("Global monitoring, rotation, notification or imported configuration changes loaded from SQLite.");
    }
'''
if main.count(old_open) != 1:
    raise RuntimeError(f'MainForm OpenSettings anchor count={main.count(old_open)}')
main = main.replace(old_open, new_open, 1)
main_path.write_text(main, encoding='utf-8')

# Source-contract/UI regression coverage.
test_path = Path('tests/GPTDeskTop.RuntimeTests/ConfigurationImportRuntimeBoundaryTests.cs')
test_path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

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
''', encoding='utf-8')

# Development status: reconcile #82 merge and track #83.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Consistent Configuration Backup Snapshot is implemented in PR #82:',
    '- Consistent Configuration Backup Snapshot is merged on `main` (`26f0f286a1b12daaa90fca2dfbef67680ee82d4e`):',
    1)
active = '- Configuration Import Runtime Safety Boundary is implemented for Issue #83: Settings receives a live running-monitor predicate from MainForm without depending on monitor internals, blocks configuration import while any monitor worker is active, rechecks immediately before transactional apply, never auto-stops monitoring, and reloads MainForm monitor/default-setting presentation from SQLite after a successful Settings/import dialog.'
if active not in status:
    anchor = '- Consistent Configuration Backup Snapshot is merged on `main` (`26f0f286a1b12daaa90fca2dfbef67680ee82d4e`):'
    pos = status.index(anchor)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After consistent configuration-backup snapshot collection, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #83 is the current tracked post-1.8 task: block configuration import while monitor runtime is active so transactional SQLite changes cannot coexist with stale in-memory worker configuration. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected post-#81 Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #83 runtime import guard patch applied.')
