from pathlib import Path

# TrayNotificationService: expose a narrow runtime reload boundary.
tray_path = Path('src/GPTDeskTop/Services/TrayNotificationService.cs')
tray = tray_path.read_text(encoding='utf-8')
old_reload = '    private async Task ReloadSettingsAsync()\n'
new_reload = '    public async Task ReloadSettingsAsync()\n'
if tray.count(old_reload) != 1:
    raise RuntimeError(f'TrayNotificationService reload anchor count={tray.count(old_reload)}')
tray = tray.replace(old_reload, new_reload, 1)
tray_path.write_text(tray, encoding='utf-8')

# MainForm: preserve existing 3-argument constructor and add optional async callback overload.
main_path = Path('src/GPTDeskTop/UI/MainForm.cs')
main = main_path.read_text(encoding='utf-8')
field_anchor = '    private readonly LocalDatabase _database;\n'
field_add = '    private readonly Func<Task>? _onSettingsApplied;\n'
if field_add not in main:
    if main.count(field_anchor) != 1:
        raise RuntimeError(f'MainForm field anchor count={main.count(field_anchor)}')
    main = main.replace(field_anchor, field_anchor + field_add, 1)

old_ctor = '''    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;
'''
new_ctor = '''    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
        : this(chrome, monitor, database, null)
    {
    }

    public MainForm(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database,
        Func<Task>? onSettingsApplied)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;
        _onSettingsApplied = onSettingsApplied;
'''
if main.count(old_ctor) != 1:
    raise RuntimeError(f'MainForm constructor anchor count={main.count(old_ctor)}')
main = main.replace(old_ctor, new_ctor, 1)

old_open = '''    private async Task OpenSettingsAsync()
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
new_open = '''    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database, () => _monitor.IsRunning);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        await RefreshMonitorsAsync();
        if (_selectedMonitor is null)
            _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        else
            SelectCurrentMonitor();

        if (_onSettingsApplied is not null)
        {
            try
            {
                await _onSettingsApplied();
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "MainForm.RefreshSettingsRuntime");
                AppendActivity("Settings were saved, but runtime notification settings could not be refreshed. Restart GPTDeskTop or reopen Settings from the tray menu.");
            }
        }

        AppendActivity("Global monitoring, rotation, notification or imported configuration changes loaded from SQLite.");
    }
'''
if main.count(old_open) != 1:
    raise RuntimeError(f'MainForm OpenSettings anchor count={main.count(old_open)}')
main = main.replace(old_open, new_open, 1)
main_path.write_text(main, encoding='utf-8')

# Program: wire the live notification reload callback into MainForm.
program_path = Path('src/GPTDeskTop/Program.cs')
program = program_path.read_text(encoding='utf-8')
old_program = '            var mainForm = new MainForm(chrome, monitor, database);\n'
new_program = '            var mainForm = new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync);\n'
if program.count(old_program) != 1:
    raise RuntimeError(f'Program MainForm wiring anchor count={program.count(old_program)}')
program = program.replace(old_program, new_program, 1)
program_path.write_text(program, encoding='utf-8')

# Source-contract/UI regression coverage.
test_path = Path('tests/GPTDeskTop.RuntimeTests/NotificationSettingsRuntimeRefreshTests.cs')
test_path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class NotificationSettingsRuntimeRefreshTests
{
    [Fact]
    public void TrayNotificationReloadBoundaryReadsAllRuntimeNotificationSettings()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "TrayNotificationService.cs");

        Assert.Contains("public async Task ReloadSettingsAsync()", source, StringComparison.Ordinal);
        Assert.Contains("GetIntSettingAsync(\"NotificationDurationSeconds\"", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"NotificationSoundEnabled\")", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"NotificationSoundType\")", source, StringComparison.Ordinal);
        Assert.Contains("public async Task InitializeAsync() => await ReloadSettingsAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await ReloadSettingsAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormPreservesLegacyConstructorAndAddsNarrowSettingsAppliedCallback()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)", source, StringComparison.Ordinal);
        Assert.Contains(": this(chrome, monitor, database, null)", source, StringComparison.Ordinal);
        Assert.Contains("Func<Task>? onSettingsApplied", source, StringComparison.Ordinal);
        Assert.Contains("_onSettingsApplied = onSettingsApplied", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayNotificationService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainSettingsInvokesRuntimeRefreshOnlyAfterSuccessfulDialogResult()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var start = source.IndexOf("private async Task OpenSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LaunchChromeAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var openSettings = source[start..end];

        var successGuard = openSettings.IndexOf("if (form.ShowDialog(this) != DialogResult.OK) return;", StringComparison.Ordinal);
        var callback = openSettings.IndexOf("await _onSettingsApplied();", StringComparison.Ordinal);
        Assert.True(successGuard >= 0);
        Assert.True(callback > successGuard);
        Assert.Contains("if (_onSettingsApplied is not null)", openSettings, StringComparison.Ordinal);
        Assert.Contains("MainForm.RefreshSettingsRuntime", openSettings, StringComparison.Ordinal);
        Assert.Contains("await RefreshMonitorsAsync()", openSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramWiresTrayNotificationReloadIntoMainSettingsFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("using var notifications = new TrayNotificationService(monitor, database);", source, StringComparison.Ordinal);
        Assert.Contains("notifications.InitializeAsync().GetAwaiter().GetResult();", source, StringComparison.Ordinal);
        Assert.Contains("new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync)", source, StringComparison.Ordinal);
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

# Development status: reconcile #84 merge and track #85.
status_path = Path('docs/DEVELOPMENT_STATUS.md')
status = status_path.read_text(encoding='utf-8')
status = status.replace(
    '- Configuration Import Runtime Safety Boundary is implemented in PR #84:',
    '- Configuration Import Runtime Safety Boundary is merged on `main` (`229351357f4ddf1f42511665bd53b131d360a1ca`):',
    1)
active = '- Live Tray Notification Settings Refresh is implemented for Issue #85: TrayNotificationService exposes a narrow async reload boundary for duration/sound settings, MainForm accepts an optional success-only Settings-applied callback without depending on tray internals, and Program wires the tray reload callback so normal Settings saves and successful configuration imports from the main window apply notification behavior immediately without an application restart.'
if active not in status:
    anchor = '- Configuration Import Runtime Safety Boundary is merged on `main` (`229351357f4ddf1f42511665bd53b131d360a1ca`):'
    pos = status.index(anchor)
    line_end = status.index('\n', pos)
    status = status[:line_end + 1] + active + '\n' + status[line_end + 1:]
old_next = 'After the configuration-import runtime safety boundary, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
new_next = 'Issue #85 is the current tracked post-1.8 task: reload cached tray notification duration/sound settings immediately after successful main-window Settings saves or configuration imports. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.'
if old_next not in status:
    raise RuntimeError('Expected post-#83 Next Executable Task text not found')
status = status.replace(old_next, new_next, 1)
status_path.write_text(status, encoding='utf-8')

print('Issue #85 notification runtime refresh patch applied.')
