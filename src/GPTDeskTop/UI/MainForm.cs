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
    private readonly Button _refreshTabsButton = new() { Text = "Refresh Chrome Tabs", AutoSize = true };
    private readonly Button _startButton = new() { Text = "Start Monitoring", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop Monitoring", AutoSize = true, Enabled = false };
    private readonly Button _refreshHistoryButton = new() { Text = "Refresh History", AutoSize = true };
    private readonly DataGridView _tabsGrid = new();
    private readonly DataGridView _historyGrid = new();
    private readonly RichTextBox _activityBox = new();
    private readonly TextBox _autoReplyBox = new() { Text = "كمل", Font = new Font("Segoe UI", 11F), Dock = DockStyle.Fill };
    private readonly Label _selectedTabLabel = new() { Text = "Selected tab: none", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private List<ChromeTab> _tabs = new();
    private ChromeTab? _selectedTab;

    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;

        Text = "GPTDeskTop - Chrome ChatGPT Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1450, 900);
        Font = new Font("Segoe UI", 9.5F);

        BuildUi();
        WireEvents();
        Shown += async (_, _) => await RefreshHistoryAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        toolbar.Controls.AddRange(new Control[]
        {
            _launchChromeButton,
            _refreshTabsButton,
            _startButton,
            _stopButton,
            _refreshHistoryButton
        });

        ConfigureTabsGrid();

        var replyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 6) };
        replyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        replyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        replyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        replyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        replyPanel.Controls.Add(new Label { Text = "Auto reply text:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        replyPanel.Controls.Add(_autoReplyBox, 1, 0);
        replyPanel.Controls.Add(new Label { Text = "Current target:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        replyPanel.Controls.Add(_selectedTabLabel, 1, 1);

        ConfigureHistoryGrid();
        _activityBox.Dock = DockStyle.Fill;
        _activityBox.ReadOnly = true;
        _activityBox.BackColor = Color.FromArgb(20, 23, 28);
        _activityBox.ForeColor = Color.Gainsboro;
        _activityBox.Font = new Font("Consolas", 9.5F);

        var activityGroup = new GroupBox { Text = "Live Activity", Dock = DockStyle.Fill, Padding = new Padding(8) };
        activityGroup.Controls.Add(_activityBox);
        var historyGroup = new GroupBox { Text = "Stored History", Dock = DockStyle.Fill, Padding = new Padding(8) };
        historyGroup.Controls.Add(_historyGrid);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 620 };
        split.Panel1.Controls.Add(activityGroup);
        split.Panel2.Controls.Add(historyGroup);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_tabsGrid, 0, 1);
        root.Controls.Add(replyPanel, 0, 2);
        root.Controls.Add(split, 0, 3);
        Controls.Add(root);
    }

    private void ConfigureTabsGrid()
    {
        _tabsGrid.Dock = DockStyle.Fill;
        _tabsGrid.ReadOnly = true;
        _tabsGrid.AllowUserToAddRows = false;
        _tabsGrid.AllowUserToDeleteRows = false;
        _tabsGrid.MultiSelect = false;
        _tabsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _tabsGrid.AutoGenerateColumns = false;
        _tabsGrid.RowHeadersVisible = false;
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Id), HeaderText = "Tab ID", Width = 260 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Title), HeaderText = "Title", Width = 420 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Url), HeaderText = "URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureHistoryGrid()
    {
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.ReadOnly = true;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _historyGrid.AutoGenerateColumns = false;
        _historyGrid.RowHeadersVisible = false;
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Id), HeaderText = "ID", Width = 60 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Timestamp), HeaderText = "Timestamp", Width = 155, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Direction), HeaderText = "Direction", Width = 90 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Prompt), HeaderText = "Prompt", Width = 160 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Response), HeaderText = "Response", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Status), HeaderText = "Status", Width = 85 });
    }

    private void WireEvents()
    {
        _launchChromeButton.Click += async (_, _) => await LaunchChromeAsync();
        _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _startButton.Click += async (_, _) => await StartMonitoringAsync();
        _stopButton.Click += async (_, _) => await StopMonitoringAsync();
        _refreshHistoryButton.Click += async (_, _) => await RefreshHistoryAsync();
        _tabsGrid.SelectionChanged += (_, _) => SelectCurrentTab();

        _monitor.Activity += message => Ui(() => AppendActivity(message));
        _monitor.HistoryChanged += () => Ui(async () => await RefreshHistoryAsync());
    }

    private async Task LaunchChromeAsync()
    {
        try
        {
            _chrome.LaunchMonitorChrome();
            AppendActivity("Monitor Chrome launched with remote debugging enabled.");
            await Task.Delay(1800);
            await RefreshTabsAsync();
        }
        catch (Exception ex)
        {
            AppendActivity($"Launch error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Chrome Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshTabsAsync()
    {
        try
        {
            _tabs = await _chrome.GetTabsAsync();
            _tabsGrid.DataSource = null;
            _tabsGrid.DataSource = _tabs;
            AppendActivity($"Chrome tabs loaded: {_tabs.Count}");

            if (_tabs.Count > 0)
            {
                var preferred = _tabs.FindIndex(t => t.Url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase));
                var rowIndex = preferred >= 0 ? preferred : 0;
                _tabsGrid.ClearSelection();
                _tabsGrid.Rows[rowIndex].Selected = true;
                _tabsGrid.CurrentCell = _tabsGrid.Rows[rowIndex].Cells[0];
                SelectCurrentTab();
            }
        }
        catch (Exception ex)
        {
            AppendActivity($"Cannot read Chrome tabs: {ex.Message}");
            MessageBox.Show(this,
                "No Chrome DevTools endpoint was found. Click 'Launch Monitor Chrome' first.\n\n" + ex.Message,
                "Chrome Not Connected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void SelectCurrentTab()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab)
            return;
        _selectedTab = tab;
        _selectedTabLabel.Text = $"{tab.Title} | ID: {tab.Id}";
    }

    private async Task StartMonitoringAsync()
    {
        if (_selectedTab is null)
        {
            MessageBox.Show(this, "Refresh Chrome tabs and select the ChatGPT page first.", "No Tab Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_selectedTab.Url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            var answer = MessageBox.Show(this, "The selected tab does not look like chatgpt.com. Monitor it anyway?", "Confirm Target", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }
        if (string.IsNullOrWhiteSpace(_autoReplyBox.Text))
        {
            MessageBox.Show(this, "Enter the text that should be sent after each ChatGPT reply.", "Auto Reply Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _monitor.StartAsync(_selectedTab, _autoReplyBox.Text);
            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _refreshTabsButton.Enabled = false;
            _autoReplyBox.Enabled = false;
            AppendActivity("Monitoring started.");
        }
        catch (Exception ex)
        {
            AppendActivity($"Start error: {ex.Message}");
        }
    }

    private async Task StopMonitoringAsync()
    {
        await _monitor.StopAsync();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        _refreshTabsButton.Enabled = true;
        _autoReplyBox.Enabled = true;
        AppendActivity("Monitoring stopped.");
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var logs = await _database.GetRecentLogsAsync();
            _historyGrid.DataSource = null;
            _historyGrid.DataSource = logs;
        }
        catch (Exception ex)
        {
            AppendActivity($"History error: {ex.Message}");
        }
    }

    private void AppendActivity(string message)
    {
        _activityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _activityBox.SelectionStart = _activityBox.TextLength;
        _activityBox.ScrollToCaret();
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_monitor.IsRunning)
            _monitor.StopAsync().GetAwaiter().GetResult();
        base.OnFormClosing(e);
    }
}
