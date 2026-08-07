using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class MainForm : Form
{
    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;

    private readonly Button _launchChromeButton = new() { Text = "Launch Monitor Chrome", AutoSize = true };
    private readonly Button _hideChromeButton = new() { Text = "Hide Chrome", AutoSize = true };
    private readonly Button _showChromeButton = new() { Text = "Show Chrome", AutoSize = true };
    private readonly Button _refreshTabsButton = new() { Text = "Refresh Chrome Tabs", AutoSize = true };
    private readonly Button _addMonitorButton = new() { Text = "Add Selected Tab(s)", AutoSize = true };
    private readonly Button _saveMonitorButton = new() { Text = "Save Monitor", AutoSize = true };
    private readonly Button _deleteMonitorButton = new() { Text = "Delete Monitor", AutoSize = true };
    private readonly Button _startSelectedButton = new() { Text = "Start Selected", AutoSize = true };
    private readonly Button _stopSelectedButton = new() { Text = "Stop Selected", AutoSize = true };
    private readonly Button _startAllButton = new() { Text = "Start All Enabled", AutoSize = true };
    private readonly Button _stopAllButton = new() { Text = "Stop All", AutoSize = true };
    private readonly Button _refreshHistoryButton = new() { Text = "Refresh History", AutoSize = true };
    private readonly Button _deleteLogButton = new() { Text = "Delete Selected Log", AutoSize = true };
    private readonly Button _clearHistoryButton = new() { Text = "Clear History", AutoSize = true };

    private readonly DataGridView _tabsGrid = new();
    private readonly DataGridView _monitorsGrid = new();
    private readonly DataGridView _historyGrid = new();
    private readonly RichTextBox _activityBox = new();
    private readonly TextBox _autoReplyBox = new() { Text = "كمل", Font = new Font("Segoe UI", 11F), Dock = DockStyle.Fill };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _editorLabel = new() { Text = "Select one or more open tabs to add, or select a saved monitor to edit.", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _versionLabel = new()
    {
        Text = $"GPTDeskTop v{GetAppVersion()}  |  .NET 8  |  Chrome CDP",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = Color.DimGray
    };

    private List<ChromeTab> _tabs = new();
    private List<SavedMonitor> _monitors = new();
    private ChromeTab? _selectedTab;
    private SavedMonitor? _selectedMonitor;
    private bool _chromeHidden;

    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;

        Text = $"GPTDeskTop v{GetAppVersion()} - Multi Tab ChatGPT Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 780);
        Size = new Size(1600, 980);
        Font = new Font("Segoe UI", 9.5F);

        BuildUi();
        WireEvents();
        Shown += async (_, _) => await LoadStartupStateAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 205));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        toolbar.Controls.AddRange(new Control[]
        {
            _launchChromeButton, _hideChromeButton, _showChromeButton, _refreshTabsButton, _addMonitorButton, _deleteMonitorButton,
            _startSelectedButton, _stopSelectedButton, _startAllButton, _stopAllButton,
            _refreshHistoryButton, _deleteLogButton, _clearHistoryButton
        });

        ConfigureTabsGrid();
        ConfigureMonitorsGrid();
        ConfigureHistoryGrid();

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 5, 0, 5) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        editor.Controls.Add(new Label { Text = "Auto reply:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        editor.Controls.Add(_autoReplyBox, 1, 0);
        editor.Controls.Add(_enabledCheck, 2, 0);
        editor.Controls.Add(_saveMonitorButton, 3, 0);
        editor.Controls.Add(new Label { Text = "Editor target:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        editor.Controls.Add(_editorLabel, 1, 1);
        editor.SetColumnSpan(_editorLabel, 3);

        _activityBox.Dock = DockStyle.Fill;
        _activityBox.ReadOnly = true;
        _activityBox.BackColor = Color.FromArgb(20, 23, 28);
        _activityBox.ForeColor = Color.Gainsboro;
        _activityBox.Font = new Font("Consolas", 9.5F);

        var openGroup = new GroupBox { Text = "Open Chrome Tabs - Ctrl/Shift select multiple rows, then Add Selected Tab(s)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        openGroup.Controls.Add(_tabsGrid);
        var savedGroup = new GroupBox { Text = "Saved Monitors - each row runs independently", Dock = DockStyle.Fill, Padding = new Padding(8) };
        savedGroup.Controls.Add(_monitorsGrid);
        var activityGroup = new GroupBox { Text = "Live Activity", Dock = DockStyle.Fill, Padding = new Padding(8) };
        activityGroup.Controls.Add(_activityBox);
        var historyGroup = new GroupBox { Text = "Stored History", Dock = DockStyle.Fill, Padding = new Padding(8) };
        historyGroup.Controls.Add(_historyGrid);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };
        split.Panel1.Controls.Add(activityGroup);
        split.Panel2.Controls.Add(historyGroup);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(openGroup, 0, 1);
        root.Controls.Add(editor, 0, 2);
        root.Controls.Add(savedGroup, 0, 3);
        root.Controls.Add(split, 0, 4);
        root.Controls.Add(_versionLabel, 0, 5);
        Controls.Add(root);

        UpdateChromeVisibilityButtons();
    }

    private void ConfigureTabsGrid()
    {
        ConfigureReadOnlyGrid(_tabsGrid);
        _tabsGrid.MultiSelect = true;
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Id), HeaderText = "Tab ID", Width = 260 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Title), HeaderText = "Title", Width = 420 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Url), HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureMonitorsGrid()
    {
        ConfigureReadOnlyGrid(_monitorsGrid);
        _monitorsGrid.MultiSelect = false;
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Id), HeaderText = "ID", Width = 55 });
        _monitorsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(SavedMonitor.Enabled), HeaderText = "Enabled", Width = 70 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.RuntimeStatus), HeaderText = "Status", Width = 85 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Title), HeaderText = "Title", Width = 330 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.AutoReply), HeaderText = "Auto Reply", Width = 180 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.TabId), HeaderText = "Tab ID", Width = 200 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Url), HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureHistoryGrid()
    {
        ConfigureReadOnlyGrid(_historyGrid);
        _historyGrid.MultiSelect = false;
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Id), HeaderText = "ID", Width = 55 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Timestamp), HeaderText = "Timestamp", Width = 145, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.MonitorId), HeaderText = "Monitor", Width = 65 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.TabTitle), HeaderText = "Tab", Width = 150 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Direction), HeaderText = "Direction", Width = 80 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Prompt), HeaderText = "Prompt", Width = 140 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Response), HeaderText = "Response", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Status), HeaderText = "Status", Width = 75 });
    }

    private static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
    }

    private void WireEvents()
    {
        _launchChromeButton.Click += async (_, _) => await LaunchChromeAsync();
        _hideChromeButton.Click += async (_, _) => await HideChromeAsync();
        _showChromeButton.Click += async (_, _) => await ShowChromeAsync();
        _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _addMonitorButton.Click += async (_, _) => await AddSelectedTabAsync();
        _saveMonitorButton.Click += async (_, _) => await SaveSelectedMonitorAsync();
        _deleteMonitorButton.Click += async (_, _) => await DeleteSelectedMonitorAsync();
        _startSelectedButton.Click += async (_, _) => await StartSelectedMonitorAsync();
        _stopSelectedButton.Click += async (_, _) => await StopSelectedMonitorAsync();
        _startAllButton.Click += async (_, _) => await StartAllEnabledAsync();
        _stopAllButton.Click += async (_, _) => await StopAllAsync();
        _refreshHistoryButton.Click += async (_, _) => await RefreshHistoryAsync();
        _deleteLogButton.Click += async (_, _) => await DeleteSelectedLogAsync();
        _clearHistoryButton.Click += async (_, _) => await ClearHistoryAsync();
        _tabsGrid.SelectionChanged += (_, _) => SelectCurrentTab();
        _monitorsGrid.SelectionChanged += (_, _) => SelectCurrentMonitor();

        _monitor.Activity += (id, message) => Ui(() => AppendActivity($"M{id}: {message}"));
        _monitor.HistoryChanged += () => Ui(async () => await RefreshHistoryAsync());
        _monitor.RunningStateChanged += () => Ui(async () => await RefreshMonitorsAsync(false));
    }

    private async Task LoadStartupStateAsync()
    {
        _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        _chromeHidden = string.Equals(await _database.GetSettingAsync("ChromeHidden"), "1", StringComparison.Ordinal);
        UpdateChromeVisibilityButtons();
        await RefreshMonitorsAsync();
        await RefreshHistoryAsync();
        AppendActivity($"Application version: {GetAppVersion()}");
        AppendActivity($"Saved Chrome visibility: {(_chromeHidden ? "Hidden" : "Visible")}");
    }

    private async Task LaunchChromeAsync()
    {
        try
        {
            _chrome.LaunchMonitorChrome();
            AppendActivity("Monitor Chrome launched.");
            await Task.Delay(1800);

            if (_chromeHidden)
            {
                var hidden = await _chrome.HideMonitorChromeAsync();
                AppendActivity(hidden ? "Monitor Chrome restored in hidden mode." : "Chrome launched but could not be hidden automatically.");
            }

            await RefreshTabsAsync();
        }
        catch (Exception ex)
        {
            ShowError("Chrome Launch Error", ex.Message);
        }
    }

    private async Task HideChromeAsync()
    {
        try
        {
            if (!await _chrome.HideMonitorChromeAsync())
            {
                MessageBox.Show(this, "Monitor Chrome window was not found. Launch Monitor Chrome first.", "Chrome Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _chromeHidden = true;
            await _database.SetSettingAsync("ChromeHidden", "1");
            UpdateChromeVisibilityButtons();
            AppendActivity("Monitor Chrome hidden. Monitoring continues in background.");
        }
        catch (Exception ex)
        {
            ShowError("Hide Chrome Error", ex.Message);
        }
    }

    private async Task ShowChromeAsync()
    {
        try
        {
            if (!await _chrome.ShowMonitorChromeAsync())
            {
                MessageBox.Show(this, "Monitor Chrome window was not found. Launch Monitor Chrome first.", "Chrome Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _chromeHidden = false;
            await _database.SetSettingAsync("ChromeHidden", "0");
            UpdateChromeVisibilityButtons();
            AppendActivity("Monitor Chrome shown.");
        }
        catch (Exception ex)
        {
            ShowError("Show Chrome Error", ex.Message);
        }
    }

    private void UpdateChromeVisibilityButtons()
    {
        _hideChromeButton.Enabled = !_chromeHidden;
        _showChromeButton.Enabled = _chromeHidden;
    }

    private async Task RefreshTabsAsync()
    {
        try
        {
            _tabs = await _chrome.GetTabsAsync();
            _tabsGrid.DataSource = null;
            _tabsGrid.DataSource = _tabs;
            AppendActivity($"Open Chrome tabs loaded: {_tabs.Count}");
            if (_tabs.Count > 0)
            {
                _tabsGrid.ClearSelection();
                _tabsGrid.Rows[0].Selected = true;
                _tabsGrid.CurrentCell = _tabsGrid.Rows[0].Cells[0];
            }
        }
        catch (Exception ex)
        {
            AppendActivity($"Cannot read Chrome tabs: {ex.Message}");
            MessageBox.Show(this, "Click 'Launch Monitor Chrome' first.\n\n" + ex.Message, "Chrome Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SelectCurrentTab()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab)
            return;
        _selectedTab = tab;
        if (_selectedMonitor is null)
            _editorLabel.Text = $"Open tab: {tab.Title} | {tab.Id}";
    }

    private void SelectCurrentMonitor()
    {
        if (_monitorsGrid.CurrentRow?.DataBoundItem is not SavedMonitor monitor)
            return;
        _selectedMonitor = monitor;
        _autoReplyBox.Text = monitor.AutoReply;
        _enabledCheck.Checked = monitor.Enabled;
        _editorLabel.Text = $"Saved monitor #{monitor.Id}: {monitor.Title}";
    }

    private async Task AddSelectedTabAsync()
    {
        var selectedTabs = _tabsGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem)
            .OfType<ChromeTab>()
            .DistinctBy(t => t.Id)
            .ToList();

        if (selectedTabs.Count == 0 && _selectedTab is not null)
            selectedTabs.Add(_selectedTab);

        if (selectedTabs.Count == 0)
        {
            MessageBox.Show(this, "Select one or more open Chrome tabs first.", "No Tabs Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_autoReplyBox.Text))
        {
            MessageBox.Show(this, "Enter an auto reply first.", "Auto Reply Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        long? lastSavedId = null;
        var added = 0;
        var skipped = 0;
        foreach (var tab in selectedTabs)
        {
            var duplicate = _monitors.FirstOrDefault(m => string.Equals(m.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                skipped++;
                lastSavedId = duplicate.Id;
                continue;
            }

            var monitor = new SavedMonitor
            {
                TabId = tab.Id,
                Title = tab.Title,
                Url = tab.Url,
                AutoReply = _autoReplyBox.Text.Trim(),
                Enabled = _enabledCheck.Checked
            };
            await _database.SaveMonitorAsync(monitor);
            lastSavedId = monitor.Id;
            added++;
            AppendActivity($"Saved monitor #{monitor.Id}: {monitor.Title}");
        }

        await _database.SetSettingAsync("DefaultAutoReply", _autoReplyBox.Text.Trim());
        await RefreshMonitorsAsync();
        if (lastSavedId.HasValue)
            SelectMonitorRow(lastSavedId.Value);
        AppendActivity($"Add Selected Tab(s): added {added}, already saved {skipped}.");
    }

    private async Task SaveSelectedMonitorAsync()
    {
        if (_selectedMonitor is null)
        {
            await AddSelectedTabAsync();
            return;
        }
        if (string.IsNullOrWhiteSpace(_autoReplyBox.Text))
        {
            MessageBox.Show(this, "Auto reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _selectedMonitor.AutoReply = _autoReplyBox.Text.Trim();
        _selectedMonitor.Enabled = _enabledCheck.Checked;
        var matchingTab = ResolveTab(_selectedMonitor);
        if (matchingTab is not null)
        {
            _selectedMonitor.TabId = matchingTab.Id;
            _selectedMonitor.Title = matchingTab.Title;
            _selectedMonitor.Url = matchingTab.Url;
        }
        await _database.SaveMonitorAsync(_selectedMonitor);
        await _database.SetSettingAsync("DefaultAutoReply", _selectedMonitor.AutoReply);
        AppendActivity($"Monitor #{_selectedMonitor.Id} saved.");
        var id = _selectedMonitor.Id;
        await RefreshMonitorsAsync();
        SelectMonitorRow(id);
    }

    private async Task DeleteSelectedMonitorAsync()
    {
        if (_selectedMonitor is null)
            return;
        if (MessageBox.Show(this, $"Delete saved monitor #{_selectedMonitor.Id}?\n\nThis does not close the Chrome tab.", "Delete Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var id = _selectedMonitor.Id;
        await _monitor.StopMonitorAsync(id);
        await _database.DeleteMonitorAsync(id);
        _selectedMonitor = null;
        AppendActivity($"Monitor #{id} deleted from database.");
        await RefreshMonitorsAsync();
    }

    private async Task StartSelectedMonitorAsync()
    {
        if (_selectedMonitor is null)
        {
            MessageBox.Show(this, "Select a saved monitor first.", "No Monitor Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await StartMonitorAsync(_selectedMonitor);
    }

    private async Task StartAllEnabledAsync()
    {
        await RefreshTabsAsync();
        foreach (var monitor in _monitors.Where(m => m.Enabled).ToList())
            await StartMonitorAsync(monitor, false);
        await RefreshMonitorsAsync(false);
    }

    private async Task StartMonitorAsync(SavedMonitor monitor, bool refreshTabsIfMissing = true)
    {
        if (_monitor.IsMonitorRunning(monitor.Id))
            return;

        var tab = ResolveTab(monitor);
        if (tab is null && refreshTabsIfMissing)
        {
            await RefreshTabsAsync();
            tab = ResolveTab(monitor);
        }
        if (tab is null)
        {
            AppendActivity($"Monitor #{monitor.Id} not started: matching Chrome tab is not open ({monitor.Title}).");
            return;
        }

        monitor.TabId = tab.Id;
        monitor.Title = tab.Title;
        monitor.Url = tab.Url;
        await _database.SaveMonitorAsync(monitor);
        await _monitor.StartMonitorAsync(monitor, tab);
        await RefreshMonitorsAsync(false);
    }

    private ChromeTab? ResolveTab(SavedMonitor monitor)
        => _tabs.FirstOrDefault(t => string.Equals(t.Id, monitor.TabId, StringComparison.Ordinal))
           ?? _tabs.FirstOrDefault(t => string.Equals(t.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));

    private async Task StopSelectedMonitorAsync()
    {
        if (_selectedMonitor is null)
            return;
        await _monitor.StopMonitorAsync(_selectedMonitor.Id);
        await RefreshMonitorsAsync(false);
    }

    private async Task StopAllAsync()
    {
        await _monitor.StopAllAsync();
        AppendActivity("All monitor workers stopped.");
        await RefreshMonitorsAsync(false);
    }

    private async Task RefreshMonitorsAsync(bool preserveSelection = true)
    {
        var selectedId = preserveSelection ? _selectedMonitor?.Id : null;
        _monitors = await _database.GetSavedMonitorsAsync();
        foreach (var monitor in _monitors)
            monitor.RuntimeStatus = _monitor.IsMonitorRunning(monitor.Id) ? "Running" : "Stopped";
        _monitorsGrid.DataSource = null;
        _monitorsGrid.DataSource = _monitors;
        if (selectedId.HasValue)
            SelectMonitorRow(selectedId.Value);
    }

    private void SelectMonitorRow(long id)
    {
        foreach (DataGridViewRow row in _monitorsGrid.Rows)
        {
            if (row.DataBoundItem is SavedMonitor monitor && monitor.Id == id)
            {
                _monitorsGrid.ClearSelection();
                row.Selected = true;
                _monitorsGrid.CurrentCell = row.Cells[0];
                _selectedMonitor = monitor;
                _autoReplyBox.Text = monitor.AutoReply;
                _enabledCheck.Checked = monitor.Enabled;
                _editorLabel.Text = $"Saved monitor #{monitor.Id}: {monitor.Title}";
                break;
            }
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            _historyGrid.DataSource = null;
            _historyGrid.DataSource = await _database.GetRecentLogsAsync();
        }
        catch (Exception ex)
        {
            AppendActivity($"History error: {ex.Message}");
        }
    }

    private async Task DeleteSelectedLogAsync()
    {
        if (_historyGrid.CurrentRow?.DataBoundItem is not MessageLog log)
            return;
        await _database.DeleteLogAsync(log.Id);
        AppendActivity($"History row #{log.Id} deleted.");
        await RefreshHistoryAsync();
    }

    private async Task ClearHistoryAsync()
    {
        if (MessageBox.Show(this, "Delete ALL stored message history?", "Clear History", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await _database.ClearLogsAsync();
        AppendActivity("Message history cleared.");
        await RefreshHistoryAsync();
    }

    private void AppendActivity(string message)
    {
        _activityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _activityBox.SelectionStart = _activityBox.TextLength;
        _activityBox.ScrollToCaret();
    }

    private void ShowError(string title, string message)
    {
        AppendActivity($"{title}: {message}");
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_monitor.IsRunning)
            _monitor.StopAllAsync().GetAwaiter().GetResult();
        base.OnFormClosing(e);
    }
}
