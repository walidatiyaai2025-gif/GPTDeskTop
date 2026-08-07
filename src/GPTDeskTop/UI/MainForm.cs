using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class MainForm : Form
{
    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;

    private readonly Button _launchChromeButton = new() { Text = "Launch Chrome", AutoSize = true };
    private readonly Button _hideChromeButton = new() { Text = "Hide Chrome", AutoSize = true };
    private readonly Button _showChromeButton = new() { Text = "Show Chrome", AutoSize = true };
    private readonly Button _refreshTabsButton = new() { Text = "Refresh Tabs", AutoSize = true };
    private readonly Button _addMonitorButton = new() { Text = "Add Monitor", AutoSize = true };
    private readonly Button _monitorSettingsButton = new() { Text = "Monitor Settings", AutoSize = true };
    private readonly Button _deleteMonitorButton = new() { Text = "Delete Monitor", AutoSize = true };
    private readonly Button _startSelectedButton = new() { Text = "Start Selected", AutoSize = true };
    private readonly Button _stopSelectedButton = new() { Text = "Stop Selected", AutoSize = true };
    private readonly Button _startAllButton = new() { Text = "Start All", AutoSize = true };
    private readonly Button _stopAllButton = new() { Text = "Stop All", AutoSize = true };
    private readonly Button _settingsButton = new() { Text = "Settings", AutoSize = true };

    private readonly DataGridView _tabsGrid = new();
    private readonly DataGridView _monitorsGrid = new();
    private readonly DataGridView _historyGrid = new();
    private readonly RichTextBox _activityBox = new();
    private readonly TextBox _autoReplyBox = new() { Text = "كمل", Dock = DockStyle.Fill };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _editorLabel = new() { Text = "Select tabs and add monitors. Each monitor keeps its own Delay and Timer.", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _versionLabel = new() { Text = $"GPTDeskTop v{GetAppVersion()}  •  .NET 8  •  Chrome CDP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold), ForeColor = FluentTheme.Muted };

    private List<ChromeTab> _tabs = new();
    private List<SavedMonitor> _monitors = new();
    private ChromeTab? _selectedTab;
    private SavedMonitor? _selectedMonitor;
    private bool _chromeHidden;

    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
    {
        _chrome = chrome; _monitor = monitor; _database = database;
        Text = $"GPTDeskTop v{GetAppVersion()}";
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(1220, 800); Size = new Size(1600, 980);
        BuildUi(); BuildContextMenus(); WireEvents(); FluentTheme.Apply(this);
        FluentTheme.StyleButton(_launchChromeButton, primary: true);
        FluentTheme.StyleButton(_startAllButton, primary: true);
        FluentTheme.StyleButton(_deleteMonitorButton, danger: true);
        Shown += async (_, _) => await LoadStartupStateAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14), BackColor = FluentTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 4, 0, 0), BackColor = FluentTheme.Background };
        toolbar.Controls.AddRange(new Control[] { _launchChromeButton, _hideChromeButton, _showChromeButton, _refreshTabsButton, _addMonitorButton, _monitorSettingsButton, _deleteMonitorButton, _startSelectedButton, _stopSelectedButton, _startAllButton, _stopAllButton, _settingsButton });

        ConfigureTabsGrid(); ConfigureMonitorsGrid(); ConfigureHistoryGrid();

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(10, 6, 10, 6), BackColor = FluentTheme.Surface };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); editor.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        editor.Controls.Add(new Label { Text = "Auto reply", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = FluentTheme.Muted }, 0, 0);
        editor.Controls.Add(_autoReplyBox, 1, 0); editor.Controls.Add(_enabledCheck, 2, 0); editor.Controls.Add(_monitorSettingsButton, 3, 0);
        editor.Controls.Add(new Label { Text = "Selected", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = FluentTheme.Muted }, 0, 1); editor.Controls.Add(_editorLabel, 1, 1); editor.SetColumnSpan(_editorLabel, 3);

        _activityBox.Dock = DockStyle.Fill; _activityBox.ReadOnly = true; _activityBox.BackColor = Color.FromArgb(25, 28, 32); _activityBox.ForeColor = Color.FromArgb(225, 230, 235); _activityBox.Font = new Font("Cascadia Mono", 9F);

        var openGroup = MakeGroup("Open Chrome Tabs", _tabsGrid);
        var savedGroup = MakeGroup("Saved Monitors", _monitorsGrid);
        var activityGroup = MakeGroup("Live Activity", _activityBox);
        var historyGroup = MakeGroup("Stored History", _historyGrid);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700, BackColor = FluentTheme.Background };
        split.Panel1.Padding = new Padding(0, 0, 5, 0); split.Panel2.Padding = new Padding(5, 0, 0, 0); split.Panel1.Controls.Add(activityGroup); split.Panel2.Controls.Add(historyGroup);

        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(openGroup, 0, 1); root.Controls.Add(editor, 0, 2); root.Controls.Add(savedGroup, 0, 3); root.Controls.Add(split, 0, 4); root.Controls.Add(_versionLabel, 0, 5);
        Controls.Add(root); UpdateChromeVisibilityButtons();
    }

    private static GroupBox MakeGroup(string title, Control child)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = FluentTheme.Background, ForeColor = FluentTheme.Text };
        child.Dock = DockStyle.Fill; group.Controls.Add(child); return group;
    }

    private void ConfigureTabsGrid()
    {
        ConfigureReadOnlyGrid(_tabsGrid); _tabsGrid.MultiSelect = true;
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Id), HeaderText = "Tab ID", Width = 220 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Title), HeaderText = "Title", Width = 410 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Url), HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureMonitorsGrid()
    {
        ConfigureReadOnlyGrid(_monitorsGrid); _monitorsGrid.MultiSelect = false;
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Id), HeaderText = "ID", Width = 50 });
        _monitorsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(SavedMonitor.Enabled), HeaderText = "On", Width = 45 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.RuntimeStatus), HeaderText = "Status", Width = 80 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Title), HeaderText = "Title", Width = 260 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.AutoReply), HeaderText = "Auto Reply", Width = 150 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.ReplyDelaySeconds), HeaderText = "Delay", Width = 60 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.TimerSeconds), HeaderText = "Timer", Width = 60 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Url), HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureHistoryGrid()
    {
        ConfigureReadOnlyGrid(_historyGrid); _historyGrid.MultiSelect = false;
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Timestamp), HeaderText = "Timestamp", Width = 145, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.MonitorId), HeaderText = "Monitor", Width = 65 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.TabTitle), HeaderText = "Tab", Width = 160 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Direction), HeaderText = "Direction", Width = 80 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Prompt), HeaderText = "Prompt", Width = 140 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Response), HeaderText = "Response", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Status), HeaderText = "Status", Width = 120 });
    }

    private static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoGenerateColumns = false; grid.RowHeadersVisible = false;
    }

    private void BuildContextMenus()
    {
        var tabsMenu = FluentTheme.CreateMenu();
        tabsMenu.Items.Add("Add selected tab(s) to monitors", null, async (_, _) => await AddSelectedTabAsync());
        tabsMenu.Items.Add("Refresh tabs", null, async (_, _) => await RefreshTabsAsync());
        tabsMenu.Items.Add(new ToolStripSeparator());
        tabsMenu.Items.Add("Close selected tab", null, async (_, _) => await CloseSelectedTabAsync());
        _tabsGrid.ContextMenuStrip = tabsMenu;

        var monitorsMenu = FluentTheme.CreateMenu();
        monitorsMenu.Items.Add("Start", null, async (_, _) => await StartSelectedMonitorAsync());
        monitorsMenu.Items.Add("Stop", null, async (_, _) => await StopSelectedMonitorAsync());
        monitorsMenu.Items.Add("Edit settings", null, async (_, _) => await EditSelectedMonitorSettingsAsync());
        monitorsMenu.Items.Add(new ToolStripSeparator());
        monitorsMenu.Items.Add("Delete monitor", null, async (_, _) => await DeleteSelectedMonitorAsync());
        monitorsMenu.Items.Add("Add selected open tab", null, async (_, _) => await AddSelectedTabAsync());
        _monitorsGrid.ContextMenuStrip = monitorsMenu;

        var historyMenu = FluentTheme.CreateMenu();
        historyMenu.Items.Add("Refresh", null, async (_, _) => await RefreshHistoryAsync());
        historyMenu.Items.Add("Delete selected log", null, async (_, _) => await DeleteSelectedLogAsync());
        historyMenu.Items.Add("Clear all history", null, async (_, _) => await ClearHistoryAsync());
        _historyGrid.ContextMenuStrip = historyMenu;
    }

    private void WireEvents()
    {
        _launchChromeButton.Click += async (_, _) => await LaunchChromeAsync(); _hideChromeButton.Click += async (_, _) => await HideChromeAsync(); _showChromeButton.Click += async (_, _) => await ShowChromeAsync(); _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _addMonitorButton.Click += async (_, _) => await AddSelectedTabAsync(); _monitorSettingsButton.Click += async (_, _) => await EditSelectedMonitorSettingsAsync(); _deleteMonitorButton.Click += async (_, _) => await DeleteSelectedMonitorAsync();
        _startSelectedButton.Click += async (_, _) => await StartSelectedMonitorAsync(); _stopSelectedButton.Click += async (_, _) => await StopSelectedMonitorAsync(); _startAllButton.Click += async (_, _) => await StartAllEnabledAsync(); _stopAllButton.Click += async (_, _) => await StopAllAsync();
        _settingsButton.Click += async (_, _) => await OpenSettingsAsync();
        _tabsGrid.SelectionChanged += (_, _) => SelectCurrentTab(); _monitorsGrid.SelectionChanged += (_, _) => SelectCurrentMonitor(); _monitorsGrid.CellDoubleClick += async (_, _) => await EditSelectedMonitorSettingsAsync();
        _monitor.Activity += (id, message) => Ui(() => AppendActivity($"M{id}: {message}")); _monitor.HistoryChanged += () => Ui(async () => await RefreshHistoryAsync()); _monitor.RunningStateChanged += () => Ui(async () => await RefreshMonitorsAsync(false));
    }

    private async Task LoadStartupStateAsync()
    {
        _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        _chromeHidden = string.Equals(await _database.GetSettingAsync("ChromeHidden"), "1", StringComparison.Ordinal);
        UpdateChromeVisibilityButtons(); await RefreshMonitorsAsync(); await RefreshHistoryAsync(); AppendActivity($"GPTDeskTop v{GetAppVersion()} ready.");
    }

    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        AppendActivity("Default monitor and notification settings saved.");
    }

    private async Task LaunchChromeAsync()
    {
        try { _chrome.LaunchMonitorChrome(); AppendActivity("Monitor Chrome launched."); await Task.Delay(1800); if (_chromeHidden) await _chrome.HideMonitorChromeAsync(); await RefreshTabsAsync(); }
        catch (Exception ex) { ShowError("Chrome Launch Error", ex.Message); }
    }

    private async Task HideChromeAsync()
    {
        try { if (!await _chrome.HideMonitorChromeAsync()) return; _chromeHidden = true; await _database.SetSettingAsync("ChromeHidden", "1"); UpdateChromeVisibilityButtons(); AppendActivity("Chrome hidden; monitoring continues."); }
        catch (Exception ex) { ShowError("Hide Chrome Error", ex.Message); }
    }

    private async Task ShowChromeAsync()
    {
        try { if (!await _chrome.ShowMonitorChromeAsync()) return; _chromeHidden = false; await _database.SetSettingAsync("ChromeHidden", "0"); UpdateChromeVisibilityButtons(); AppendActivity("Chrome shown."); }
        catch (Exception ex) { ShowError("Show Chrome Error", ex.Message); }
    }

    private void UpdateChromeVisibilityButtons() { _hideChromeButton.Enabled = !_chromeHidden; _showChromeButton.Enabled = _chromeHidden; }

    private async Task RefreshTabsAsync()
    {
        try
        {
            _tabs = await _chrome.GetTabsAsync(); _tabsGrid.DataSource = null; _tabsGrid.DataSource = _tabs; AppendActivity($"Open Chrome tabs: {_tabs.Count}");
            if (_tabs.Count > 0) { _tabsGrid.ClearSelection(); _tabsGrid.Rows[0].Selected = true; _tabsGrid.CurrentCell = _tabsGrid.Rows[0].Cells[0]; }
        }
        catch (Exception ex) { AppendActivity($"Cannot read Chrome tabs: {ex.Message}"); }
    }

    private void SelectCurrentTab()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab) return; _selectedTab = tab; if (_selectedMonitor is null) _editorLabel.Text = $"Open tab: {tab.Title}";
    }

    private void SelectCurrentMonitor()
    {
        if (_monitorsGrid.CurrentRow?.DataBoundItem is not SavedMonitor monitor) return; _selectedMonitor = monitor; _autoReplyBox.Text = monitor.AutoReply; _enabledCheck.Checked = monitor.Enabled; _editorLabel.Text = $"Monitor #{monitor.Id} • {monitor.Title} • Delay {monitor.ReplyDelaySeconds}s • Timer {monitor.TimerSeconds}s";
    }

    private async Task AddSelectedTabAsync()
    {
        var selectedTabs = _tabsGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ChromeTab>().DistinctBy(t => t.Id).ToList();
        if (selectedTabs.Count == 0 && _selectedTab is not null) selectedTabs.Add(_selectedTab);
        if (selectedTabs.Count == 0) { MessageBox.Show(this, "Select one or more Chrome tabs first.", "Add Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var defaultReply = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        var defaultDelay = await _database.GetIntSettingAsync("DefaultMonitorDelaySeconds", 3, 0, 300);
        var defaultTimer = await _database.GetIntSettingAsync("DefaultMonitorTimerSeconds", 1, 1, 60);
        long? lastId = null;
        foreach (var tab in selectedTabs)
        {
            var duplicate = _monitors.FirstOrDefault(m => string.Equals(m.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null) { lastId = duplicate.Id; continue; }
            var monitor = new SavedMonitor { TabId = tab.Id, Title = tab.Title, Url = tab.Url, AutoReply = defaultReply, ReplyDelaySeconds = defaultDelay, TimerSeconds = defaultTimer, Enabled = true };
            if (!MonitorSettingsForm.Edit(this, monitor)) continue;
            await _database.SaveMonitorAsync(monitor); lastId = monitor.Id; AppendActivity($"Added monitor #{monitor.Id}: Delay {monitor.ReplyDelaySeconds}s / Timer {monitor.TimerSeconds}s");
        }
        await RefreshMonitorsAsync(); if (lastId.HasValue) SelectMonitorRow(lastId.Value);
    }

    private async Task EditSelectedMonitorSettingsAsync()
    {
        if (_selectedMonitor is null) return;
        if (_monitor.IsMonitorRunning(_selectedMonitor.Id)) { MessageBox.Show(this, "Stop this monitor before changing Delay/Timer.", "Monitor Running", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!MonitorSettingsForm.Edit(this, _selectedMonitor)) return;
        await _database.SaveMonitorAsync(_selectedMonitor); var id = _selectedMonitor.Id; await RefreshMonitorsAsync(); SelectMonitorRow(id);
    }

    private async Task DeleteSelectedMonitorAsync()
    {
        if (_selectedMonitor is null) return;
        if (MessageBox.Show(this, $"Delete monitor #{_selectedMonitor.Id}?", "Delete Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var id = _selectedMonitor.Id; await _monitor.StopMonitorAsync(id); await _database.DeleteMonitorAsync(id); _selectedMonitor = null; await RefreshMonitorsAsync(); AppendActivity($"Monitor #{id} deleted.");
    }

    private async Task StartSelectedMonitorAsync() { if (_selectedMonitor is not null) await StartMonitorAsync(_selectedMonitor); }
    private async Task StopSelectedMonitorAsync() { if (_selectedMonitor is not null) { await _monitor.StopMonitorAsync(_selectedMonitor.Id); await RefreshMonitorsAsync(false); } }
    private async Task StopAllAsync() { await _monitor.StopAllAsync(); await RefreshMonitorsAsync(false); AppendActivity("All monitors stopped."); }

    private async Task StartAllEnabledAsync()
    {
        await RefreshTabsAsync(); foreach (var monitor in _monitors.Where(m => m.Enabled).ToList()) await StartMonitorAsync(monitor, false); await RefreshMonitorsAsync(false);
    }

    private async Task StartMonitorAsync(SavedMonitor monitor, bool refreshTabsIfMissing = true)
    {
        if (_monitor.IsMonitorRunning(monitor.Id)) return;
        var tab = ResolveTab(monitor);
        if (tab is null && refreshTabsIfMissing) { await RefreshTabsAsync(); tab = ResolveTab(monitor); }
        if (tab is null) { AppendActivity($"Monitor #{monitor.Id}: matching tab not open."); return; }
        monitor.TabId = tab.Id; monitor.Title = tab.Title; monitor.Url = tab.Url; await _database.SaveMonitorAsync(monitor); await _monitor.StartMonitorAsync(monitor, tab); await RefreshMonitorsAsync(false);
    }

    private ChromeTab? ResolveTab(SavedMonitor monitor) => _tabs.FirstOrDefault(t => t.Id == monitor.TabId) ?? _tabs.FirstOrDefault(t => string.Equals(t.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));

    private async Task CloseSelectedTabAsync()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab) return;
        await _chrome.CloseTabAsync(tab); AppendActivity($"Closed Chrome tab: {tab.Title}"); await RefreshTabsAsync();
    }

    private async Task RefreshMonitorsAsync(bool preserveSelection = true)
    {
        var selectedId = preserveSelection ? _selectedMonitor?.Id : null; _monitors = await _database.GetSavedMonitorsAsync();
        foreach (var monitor in _monitors) monitor.RuntimeStatus = _monitor.IsMonitorRunning(monitor.Id) ? "Running" : "Stopped";
        _monitorsGrid.DataSource = null; _monitorsGrid.DataSource = _monitors; if (selectedId.HasValue) SelectMonitorRow(selectedId.Value);
    }

    private void SelectMonitorRow(long id)
    {
        foreach (DataGridViewRow row in _monitorsGrid.Rows)
        {
            if (row.DataBoundItem is not SavedMonitor monitor || monitor.Id != id) continue;
            _monitorsGrid.ClearSelection(); row.Selected = true; _monitorsGrid.CurrentCell = row.Cells[0]; _selectedMonitor = monitor; SelectCurrentMonitor(); break;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try { _historyGrid.DataSource = null; _historyGrid.DataSource = await _database.GetRecentLogsAsync(); }
        catch (Exception ex) { AppendActivity($"History error: {ex.Message}"); }
    }

    private async Task DeleteSelectedLogAsync()
    {
        if (_historyGrid.CurrentRow?.DataBoundItem is not MessageLog log) return; await _database.DeleteLogAsync(log.Id); await RefreshHistoryAsync();
    }

    private async Task ClearHistoryAsync()
    {
        if (MessageBox.Show(this, "Delete all stored history?", "Clear History", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; await _database.ClearLogsAsync(); await RefreshHistoryAsync();
    }

    private void AppendActivity(string message) { _activityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); _activityBox.SelectionStart = _activityBox.TextLength; _activityBox.ScrollToCaret(); }
    private void ShowError(string title, string message) { AppendActivity($"{title}: {message}"); MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private void Ui(Action action) { if (IsDisposed || Disposing) return; if (InvokeRequired) BeginInvoke(action); else action(); }
    private static string GetAppVersion() { var version = typeof(MainForm).Assembly.GetName().Version; return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}"; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            if (_monitor.IsRunning) _monitor.StopAllAsync().GetAwaiter().GetResult();
            _chrome.CloseAllMonitorTabsAsync().GetAwaiter().GetResult();
        }
        catch { }
        base.OnFormClosing(e);
    }
}
