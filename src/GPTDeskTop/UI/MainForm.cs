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
    private readonly Button _refreshTabsButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _addMonitorButton = new() { Text = "Add Monitor", AutoSize = true };
    private readonly Button _monitorSettingsButton = new() { Text = "Edit Monitor", AutoSize = true };
    private readonly Button _quickMonitorSettingsButton = new() { Text = "Edit Selected Monitor", AutoSize = true };
    private readonly Button _deleteMonitorButton = new() { Text = "Delete", AutoSize = true };
    private readonly Button _startSelectedButton = new() { Text = "Start Selected", AutoSize = true };
    private readonly Button _stopSelectedButton = new() { Text = "Stop Selected", AutoSize = true };
    private readonly Button _startAllButton = new() { Text = "Start All", AutoSize = true };
    private readonly Button _stopAllButton = new() { Text = "Stop All", AutoSize = true };
    private readonly Button _settingsButton = new() { Text = "Settings", AutoSize = true };

    private readonly DataGridView _tabsGrid = new();
    private readonly DataGridView _monitorsGrid = new();
    private readonly DataGridView _historyGrid = new();
    private readonly RichTextBox _activityBox = new();
    private readonly TextBox _autoReplyBox = new() { Text = "كمل", Dock = DockStyle.Fill, ReadOnly = true, TabStop = false };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, Enabled = false };
    private readonly Label _editorLabel = new() { Text = "Select a monitor to review its runtime settings.", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _chromeMetricValue = CreateMetricValue("Visible");
    private readonly Label _tabsMetricValue = CreateMetricValue("0");
    private readonly Label _monitorsMetricValue = CreateMetricValue("0");
    private readonly Label _runningMetricValue = CreateMetricValue("0");
    private readonly Label _versionLabel = new() { Text = $"GPTDeskTop v{GetAppVersion()}  •  .NET 8  •  Chrome CDP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold), ForeColor = FluentTheme.Muted };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 8000, InitialDelay = 450, ReshowDelay = 100 };
    private readonly SplitContainer _workspaceSplit = new();
    private readonly SplitContainer _diagnosticsSplit = new();
    private readonly Label _tabsEmptyState = CreateEmptyState("No ChatGPT tabs are open", "Launch the monitor Chrome window or choose Refresh after opening a conversation.");
    private readonly Label _monitorsEmptyState = CreateEmptyState("No saved monitors yet", "Select an open ChatGPT tab and choose Add Monitor to start tracking it.");
    private readonly Label _historyEmptyState = CreateEmptyState("No stored history yet", "Inbound, outbound and recovery receipts will appear here as monitors run.");

    private List<ChromeTab> _tabs = new();
    private List<SavedMonitor> _monitors = new();
    private ChromeTab? _selectedTab;
    private SavedMonitor? _selectedMonitor;
    private bool _chromeHidden;
    private bool _shutdownRequested;
    private bool _shutdownCompleted;

    public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)
    {
        _chrome = chrome;
        _monitor = monitor;
        _database = database;
        Text = $"GPTDeskTop v{GetAppVersion()}";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(980, 680);
        KeyPreview = true;
        ApplyInitialWindowLayout();

        BuildUi();
        BuildContextMenus();
        WireEvents();
        ConfigureTooltips();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_launchChromeButton, primary: true);
        FluentTheme.StyleButton(_startAllButton, primary: true);
        FluentTheme.StyleButton(_deleteMonitorButton, danger: true);
        FluentTheme.StyleButton(_quickMonitorSettingsButton, primary: true);
        UpdateActionStates();
            Shown += async (_, _) =>
    {
        await RestoreOperatorLayoutAsync();
        await LoadStartupStateAsync();
        FocusOperationalWorkspace();
    };
        SizeChanged += (_, _) => ClampResponsiveSplitters();
    }

    private void BuildUi()
    {
        ConfigureTabsGrid();
        ConfigureMonitorsGrid();
        ConfigureHistoryGrid();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildWorkspace(), 0, 2);
        root.Controls.Add(BuildDiagnostics(), 0, 3);
        root.Controls.Add(_versionLabel, 0, 4);
        Controls.Add(root);
        UpdateChromeVisibilityButtons();
        UpdateEmptyStates();
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(18, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 8)
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));

        var titleBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Surface };
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        titleBlock.Controls.Add(new Label
        {
            Text = "GPTDeskTop",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        titleBlock.Controls.Add(new Label
        {
            Text = "ChatGPT monitoring, recovery and conversation automation",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);

        var metrics = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 2, 0, 0)
        };
        metrics.Controls.Add(CreateMetricChip("Running", _runningMetricValue));
        metrics.Controls.Add(CreateMetricChip("Monitors", _monitorsMetricValue));
        metrics.Controls.Add(CreateMetricChip("Open tabs", _tabsMetricValue));
        metrics.Controls.Add(CreateMetricChip("Chrome window", _chromeMetricValue));

        layout.Controls.Add(titleBlock, 0, 0);
        layout.Controls.Add(metrics, 1, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Control BuildToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 66),
            WrapContents = true,
            AutoScroll = false,
            BackColor = FluentTheme.Background,
            Padding = new Padding(0, 2, 0, 4),
            Margin = Padding.Empty
        };
        toolbar.Controls.Add(CreateActionGroup("BROWSER", _launchChromeButton, _hideChromeButton, _showChromeButton, _refreshTabsButton));
        toolbar.Controls.Add(CreateActionGroup("MONITOR", _addMonitorButton, _monitorSettingsButton, _deleteMonitorButton));
        toolbar.Controls.Add(CreateActionGroup("RUNTIME", _startSelectedButton, _stopSelectedButton, _startAllButton, _stopAllButton));
        toolbar.Controls.Add(CreateActionGroup("APP", _settingsButton));
        return toolbar;
    }

    private Control BuildWorkspace()
    {
        var split = _workspaceSplit;
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.Panel1MinSize = 320;
        split.Panel2MinSize = 420;
        split.SplitterWidth = 6;
        split.BackColor = FluentTheme.Background;
        split.Margin = new Padding(0, 0, 0, 8);
        split.Panel1.Padding = new Padding(0, 0, 6, 0);
        split.Panel2.Padding = new Padding(6, 0, 0, 0);

        split.Panel1.Controls.Add(CreateSection(
            "Open Chrome Tabs",
            "Select one or more ChatGPT tabs, then add them as monitors.",
            CreateGridHost(_tabsGrid, _tabsEmptyState)));

        var monitorPane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = FluentTheme.Background };
        monitorPane.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        monitorPane.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        monitorPane.Controls.Add(CreateSection(
            "Saved Monitors",
            "Double-click a monitor to edit it. Runtime state is highlighted live.",
            CreateGridHost(_monitorsGrid, _monitorsEmptyState)), 0, 0);
        monitorPane.Controls.Add(BuildSelectedMonitorCard(), 0, 1);
        split.Panel2.Controls.Add(monitorPane);
        return split;
    }

    private Control BuildSelectedMonitorCard()
    {
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(14, 8, 14, 8),
            BackColor = FluentTheme.Surface,
            Margin = new Padding(0, 8, 0, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = FluentTheme.CreateSectionTitle("Selected Monitor");
        editor.Controls.Add(heading, 0, 0);
        editor.SetColumnSpan(heading, 4);
        editor.Controls.Add(FluentTheme.CreateMutedLabel("Auto reply"), 0, 1);
        editor.Controls.Add(_autoReplyBox, 1, 1);
        editor.Controls.Add(_enabledCheck, 2, 1);
        editor.Controls.Add(_quickMonitorSettingsButton, 3, 1);
        editor.Controls.Add(FluentTheme.CreateMutedLabel("Summary"), 0, 2);
        editor.Controls.Add(_editorLabel, 1, 2);
        editor.SetColumnSpan(_editorLabel, 3);

        var border = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(1), Margin = new Padding(0, 8, 0, 0) };
        border.Controls.Add(editor);
        return border;
    }

    private Control BuildDiagnostics()
    {
        _activityBox.Dock = DockStyle.Fill;
        _activityBox.ReadOnly = true;
        _activityBox.BackColor = Color.FromArgb(25, 28, 32);
        _activityBox.ForeColor = Color.FromArgb(225, 230, 235);
        _activityBox.BorderStyle = BorderStyle.None;
        _activityBox.Font = new Font("Cascadia Mono", 9F);

        var split = _diagnosticsSplit;
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.Panel1MinSize = 300;
        split.Panel2MinSize = 300;
        split.SplitterWidth = 6;
        split.BackColor = FluentTheme.Background;
        split.Panel1.Padding = new Padding(0, 0, 6, 0);
        split.Panel2.Padding = new Padding(6, 0, 0, 0);
        split.Panel1.Controls.Add(CreateSection("Live Activity", "Real-time monitor and recovery events.", _activityBox));
        split.Panel2.Controls.Add(CreateSection("Stored History", "Persisted inbound, outbound and system receipts.", CreateGridHost(_historyGrid, _historyEmptyState)));
        return split;
    }

    private static Control CreateActionGroup(string title, params Control[] actions)
    {
        var group = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Background,
            Margin = new Padding(0, 0, 14, 0),
            Padding = Padding.Empty
        };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        group.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8F, FontStyle.Bold),
            Margin = new Padding(4, 0, 0, 0)
        }, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = FluentTheme.Background, Margin = Padding.Empty, Padding = Padding.Empty };
        buttons.Controls.AddRange(actions);
        group.Controls.Add(buttons, 0, 1);
        return group;
    }

    private static Control CreateSection(string title, string subtitle, Control child)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 8, 12, 12),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = FluentTheme.Surface };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(FluentTheme.CreateSectionTitle(title), 0, 0);
        layout.Controls.Add(FluentTheme.CreateMutedLabel(subtitle), 0, 1);
        child.Dock = DockStyle.Fill;
        layout.Controls.Add(child, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control CreateMetricChip(string caption, Label value)
    {
        var panel = new Panel
        {
            Width = 118,
            Height = 54,
            BackColor = FluentTheme.SurfaceAlt,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(10, 6, 10, 4)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = FluentTheme.SurfaceAlt };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        layout.Controls.Add(value, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static Label CreateMetricValue(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.TopLeft
        };

    private static Label CreateEmptyState(string title, string detail)
        => new()
        {
            Text = $"{title}{Environment.NewLine}{detail}",
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular),
            Padding = new Padding(28),
            TextAlign = ContentAlignment.MiddleCenter
        };

    private static Control CreateGridHost(DataGridView grid, Label emptyState)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
        grid.Dock = DockStyle.Fill;
        emptyState.Dock = DockStyle.Fill;
        host.Controls.Add(grid);
        host.Controls.Add(emptyState);
        emptyState.BringToFront();
        return host;
    }

    private void ApplyInitialWindowLayout()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var targetWidth = Math.Min(1620, Math.Max(800, workingArea.Width - 24));
        var targetHeight = Math.Min(980, Math.Max(620, workingArea.Height - 24));
        MinimumSize = new Size(Math.Min(980, targetWidth), Math.Min(680, targetHeight));
        Size = new Size(targetWidth, targetHeight);
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    private void ApplyInitialSplitterRatios()
    {
        SetSplitRatio(_workspaceSplit, 0.42);
        SetSplitRatio(_diagnosticsSplit, 0.48);
    }
    private async Task RestoreOperatorLayoutAsync()
    {
        try
        {
            var boundsRaw = await _database.GetSettingAsync("Ui.Main.WindowBounds");
            if (TryParseBounds(boundsRaw, out var savedBounds) && IsBoundsVisible(savedBounds))
            {
                Bounds = ClampBoundsToWorkingArea(savedBounds);
                var state = await _database.GetSettingAsync("Ui.Main.WindowState");
                if (string.Equals(state, "Maximized", StringComparison.OrdinalIgnoreCase))
                    WindowState = FormWindowState.Maximized;
            }

            var workspaceRaw = await _database.GetSettingAsync("Ui.Main.WorkspaceSplitRatio");
            if (!TryApplyStoredSplitRatio(_workspaceSplit, workspaceRaw))
                SetSplitRatio(_workspaceSplit, 0.42);
            var diagnosticsRaw = await _database.GetSettingAsync("Ui.Main.DiagnosticsSplitRatio");
            if (!TryApplyStoredSplitRatio(_diagnosticsSplit, diagnosticsRaw))
                SetSplitRatio(_diagnosticsSplit, 0.48);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "MainForm.RestoreOperatorLayout");
            ApplyInitialSplitterRatios();
        }
    }

    private async Task PersistOperatorLayoutAsync(CancellationToken cancellationToken)
    {
        var normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (normalBounds.Width <= 0 || normalBounds.Height <= 0) return;
        var state = WindowState == FormWindowState.Maximized ? "Maximized" : "Normal";
        var boundsValue = $"{normalBounds.X},{normalBounds.Y},{normalBounds.Width},{normalBounds.Height}";
        var workspaceRatio = GetSplitRatio(_workspaceSplit).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var diagnosticsRatio = GetSplitRatio(_diagnosticsSplit).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var writes = new[]
        {
            _database.SetSettingAsync("Ui.Main.WindowBounds", boundsValue),
            _database.SetSettingAsync("Ui.Main.WindowState", state),
            _database.SetSettingAsync("Ui.Main.WorkspaceSplitRatio", workspaceRatio),
            _database.SetSettingAsync("Ui.Main.DiagnosticsSplitRatio", diagnosticsRatio)
        };
        await Task.WhenAll(writes).WaitAsync(cancellationToken);
    }

    private static bool TryParseBounds(string? value, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y) ||
            !int.TryParse(parts[2], out var width) || !int.TryParse(parts[3], out var height)) return false;
        if (width < 320 || height < 240) return false;
        bounds = new Rectangle(x, y, width, height);
        return true;
    }

    private static bool IsBoundsVisible(Rectangle bounds)
        => Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));

    private Rectangle ClampBoundsToWorkingArea(Rectangle bounds)
    {
        var area = Screen.FromRectangle(bounds).WorkingArea;
        var minimumWidth = Math.Min(MinimumSize.Width, area.Width);
        var minimumHeight = Math.Min(MinimumSize.Height, area.Height);
        var width = Math.Min(Math.Max(bounds.Width, minimumWidth), area.Width);
        var height = Math.Min(Math.Max(bounds.Height, minimumHeight), area.Height);
        var x = Math.Clamp(bounds.X, area.Left, area.Right - width);
        var y = Math.Clamp(bounds.Y, area.Top, area.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    private static bool TryApplyStoredSplitRatio(SplitContainer split, string? raw)
    {
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio)) return false;
        if (ratio is < 0.15 or > 0.85) return false;
        SetSplitRatio(split, ratio);
        return true;
    }

    private static double GetSplitRatio(SplitContainer split)
    {
        var usable = split.Width - split.SplitterWidth;
        if (usable <= 0) return 0.5;
        return Math.Clamp((double)split.SplitterDistance / usable, 0.15, 0.85);
    }

    private void ClampResponsiveSplitters()
    {
        ClampSplitter(_workspaceSplit);
        ClampSplitter(_diagnosticsSplit);
    }

    private static void SetSplitRatio(SplitContainer split, double ratio)
    {
        if (split.Width <= split.SplitterWidth) return;
        var maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;
        if (maximum < split.Panel1MinSize) return;
        var usable = split.Width - split.SplitterWidth;
        var target = (int)Math.Round(usable * ratio);
        split.SplitterDistance = Math.Clamp(target, split.Panel1MinSize, maximum);
    }

    private static void ClampSplitter(SplitContainer split)
    {
        if (split.Width <= split.SplitterWidth) return;
        var maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;
        if (maximum < split.Panel1MinSize) return;
        var clamped = Math.Clamp(split.SplitterDistance, split.Panel1MinSize, maximum);
        if (clamped != split.SplitterDistance) split.SplitterDistance = clamped;
    }

    private void UpdateEmptyStates()
    {
        _tabsEmptyState.Visible = _tabs.Count == 0;
        _monitorsEmptyState.Visible = _monitors.Count == 0;
        _historyEmptyState.Visible = _historyGrid.Rows.Count == 0;
        if (_tabsEmptyState.Visible) _tabsEmptyState.BringToFront();
        if (_monitorsEmptyState.Visible) _monitorsEmptyState.BringToFront();
        if (_historyEmptyState.Visible) _historyEmptyState.BringToFront();
    }

    private void ConfigureTabsGrid()
    {
        ConfigureReadOnlyGrid(_tabsGrid);
        _tabsGrid.MultiSelect = true;
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Title), HeaderText = "Chat title", Width = 280 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Id), HeaderText = "Tab ID", Width = 155 });
        _tabsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ChromeTab.Url), HeaderText = "Conversation URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureMonitorsGrid()
    {
        ConfigureReadOnlyGrid(_monitorsGrid);
        _monitorsGrid.MultiSelect = false;
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Id), HeaderText = "ID", Width = 48 });
        _monitorsGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(SavedMonitor.Enabled), HeaderText = "On", Width = 42 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.RuntimeStatus), HeaderText = "Runtime", Width = 105 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Title), HeaderText = "Chat", Width = 230 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.AutoReply), HeaderText = "Auto reply", Width = 120 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.ReplyDelaySeconds), HeaderText = "Delay", Width = 58 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.TimerSeconds), HeaderText = "Poll", Width = 55 });
        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.Url), HeaderText = "Conversation URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _monitorsGrid.CellFormatting += FormatMonitorCell;
    }

    private void ConfigureHistoryGrid()
    {
        ConfigureReadOnlyGrid(_historyGrid);
        _historyGrid.MultiSelect = false;
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Timestamp), HeaderText = "Time", Width = 145, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.MonitorId), HeaderText = "Monitor", Width = 62 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.TabTitle), HeaderText = "Chat", Width = 145 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Direction), HeaderText = "Flow", Width = 70 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Prompt), HeaderText = "Prompt", Width = 130 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Response), HeaderText = "Response", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MessageLog.Status), HeaderText = "Status", Width = 135 });
        _historyGrid.CellFormatting += FormatHistoryCell;
    }

    private static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.ShowCellToolTips = true;
    }

    private void FormatMonitorCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.CellStyle is not { } style) return;
        if (_monitorsGrid.Rows[e.RowIndex].DataBoundItem is not SavedMonitor monitor) return;
        if (_monitorsGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(SavedMonitor.RuntimeStatus)) return;
        var running = _monitor.IsMonitorRunning(monitor.Id);
        style.ForeColor = running ? FluentTheme.Success : FluentTheme.Muted;
        style.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
    }

    private void FormatHistoryCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.CellStyle is not { } style) return;
        if (_historyGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(MessageLog.Status)) return;
        var status = Convert.ToString(e.Value) ?? string.Empty;
        if (status.Contains("Error", StringComparison.OrdinalIgnoreCase) || status.Contains("Failed", StringComparison.OrdinalIgnoreCase) || status.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            style.ForeColor = FluentTheme.Danger;
        else if (status.Contains("Sent", StringComparison.OrdinalIgnoreCase) || status.Contains("Rotated", StringComparison.OrdinalIgnoreCase) || status.Contains("Recovered", StringComparison.OrdinalIgnoreCase))
            style.ForeColor = FluentTheme.Success;
        else if (status.Contains("Deferred", StringComparison.OrdinalIgnoreCase) || status.Contains("Limit", StringComparison.OrdinalIgnoreCase))
            style.ForeColor = FluentTheme.Warning;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, string shortcutDisplay, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcutDisplay };
        item.Click += handler;
        return item;
    }

    private void BuildContextMenus()
    {
        var tabsMenu = FluentTheme.CreateMenu();
        tabsMenu.Items.Add(CreateMenuItem("Add selected tab(s) to monitors", "Ctrl+N", async (_, _) => await AddSelectedTabAsync()));
        tabsMenu.Items.Add(CreateMenuItem("Refresh tabs", "F5", async (_, _) => await RefreshTabsAsync()));
        tabsMenu.Items.Add(new ToolStripSeparator());
        tabsMenu.Items.Add(CreateMenuItem("Close selected tab", string.Empty, async (_, _) => await CloseSelectedTabAsync()));
        _tabsGrid.ContextMenuStrip = tabsMenu;

        var monitorsMenu = FluentTheme.CreateMenu();
        monitorsMenu.Items.Add(CreateMenuItem("Start", string.Empty, async (_, _) => await StartSelectedMonitorAsync()));
        monitorsMenu.Items.Add(CreateMenuItem("Stop", string.Empty, async (_, _) => await StopSelectedMonitorAsync()));
        monitorsMenu.Items.Add(CreateMenuItem("Edit settings", "Ctrl+E", async (_, _) => await EditSelectedMonitorSettingsAsync()));
        monitorsMenu.Items.Add(new ToolStripSeparator());
        monitorsMenu.Items.Add(CreateMenuItem("Delete monitor", "Delete", async (_, _) => await DeleteSelectedMonitorAsync()));
        monitorsMenu.Items.Add(CreateMenuItem("Add selected open tab", "Ctrl+N", async (_, _) => await AddSelectedTabAsync()));
        _monitorsGrid.ContextMenuStrip = monitorsMenu;

        var historyMenu = FluentTheme.CreateMenu();
        historyMenu.Items.Add(CreateMenuItem("Refresh", string.Empty, async (_, _) => await RefreshHistoryAsync()));
        historyMenu.Items.Add(CreateMenuItem("Delete selected log", "Delete", async (_, _) => await DeleteSelectedLogAsync()));
        historyMenu.Items.Add(CreateMenuItem("Clear all history", string.Empty, async (_, _) => await ClearHistoryAsync()));
        _historyGrid.ContextMenuStrip = historyMenu;
    }

    private void WireEvents()
    {
        _launchChromeButton.Click += async (_, _) => await LaunchChromeAsync();
        _hideChromeButton.Click += async (_, _) => await HideChromeAsync();
        _showChromeButton.Click += async (_, _) => await ShowChromeAsync();
        _refreshTabsButton.Click += async (_, _) => await RefreshTabsAsync();
        _addMonitorButton.Click += async (_, _) => await AddSelectedTabAsync();
        _monitorSettingsButton.Click += async (_, _) => await EditSelectedMonitorSettingsAsync();
        _quickMonitorSettingsButton.Click += async (_, _) => await EditSelectedMonitorSettingsAsync();
        _deleteMonitorButton.Click += async (_, _) => await DeleteSelectedMonitorAsync();
        _startSelectedButton.Click += async (_, _) => await StartSelectedMonitorAsync();
        _stopSelectedButton.Click += async (_, _) => await StopSelectedMonitorAsync();
        _startAllButton.Click += async (_, _) => await StartAllEnabledAsync();
        _stopAllButton.Click += async (_, _) => await StopAllAsync();
        _settingsButton.Click += async (_, _) => await OpenSettingsAsync();
        _tabsGrid.SelectionChanged += (_, _) => SelectCurrentTab();
        _monitorsGrid.SelectionChanged += (_, _) => SelectCurrentMonitor();
        _monitorsGrid.CellDoubleClick += async (_, _) => await EditSelectedMonitorSettingsAsync();
        _monitor.Activity += (id, message) => Ui(() => AppendActivity($"M{id}: {message}"));
        _monitor.HistoryChanged += () => Ui(async () => await RefreshHistoryAsync());
        _monitor.RunningStateChanged += () => Ui(async () => { await RefreshMonitorsAsync(); UpdateActionStates(); });
        KeyDown += MainForm_KeyDown;
    }

    private void ConfigureTooltips()
    {
        _toolTip.SetToolTip(_launchChromeButton, "Launch the dedicated Chrome instance used by GPTDeskTop monitoring.");
        _toolTip.SetToolTip(_hideChromeButton, "Hide the monitor Chrome window without stopping CDP monitoring.");
        _toolTip.SetToolTip(_showChromeButton, "Show the monitor Chrome window.");
        _toolTip.SetToolTip(_refreshTabsButton, "Refresh the list of currently open ChatGPT tabs. Shortcut: F5.");
        _toolTip.SetToolTip(_addMonitorButton, "Create a saved monitor from the selected open ChatGPT tab(s). Shortcut: Ctrl+N.");
        _toolTip.SetToolTip(_monitorSettingsButton, "Edit the selected monitor. Stop a running monitor before changing its settings. Shortcut: Ctrl+E.");
        _toolTip.SetToolTip(_startAllButton, "Start every enabled saved monitor whose ChatGPT conversation is open.");
        _toolTip.SetToolTip(_stopAllButton, "Stop all currently running monitors.");
        _toolTip.SetToolTip(_settingsButton, "Open global monitoring, rotation, recovery and notification settings. Shortcut: Ctrl+,.");
        _toolTip.SetToolTip(_autoReplyBox, "Read-only summary. Use Edit Selected Monitor to change this value.");
    }

    private void FocusOperationalWorkspace()
    {
        if (_monitors.Count > 0)
        {
            _monitorsGrid.Focus();
            return;
        }

        if (_tabs.Count > 0)
        {
            _tabsGrid.Focus();
            return;
        }

        _launchChromeButton.Focus();
    }

    private async void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_shutdownRequested) return;

        var handled = true;
        try
        {
            if (e.Modifiers == Keys.None && e.KeyCode == Keys.F5)
                await RefreshTabsAsync();
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.N)
                await AddSelectedTabAsync();
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.E)
                await EditSelectedMonitorSettingsAsync();
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.Oemcomma)
                await OpenSettingsAsync();
            else if (e.Modifiers == Keys.None && e.KeyCode == Keys.Delete && _monitorsGrid.ContainsFocus)
                await DeleteSelectedMonitorAsync();
            else if (e.Modifiers == Keys.None && e.KeyCode == Keys.Delete && _historyGrid.ContainsFocus)
                await DeleteSelectedLogAsync();
            else
                handled = false;
        }
        catch (Exception ex)
        {
            ShowError("Keyboard Command Error", ex.Message);
        }

        if (!handled) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private async Task LoadStartupStateAsync()
    {
        _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        _chromeHidden = string.Equals(await _database.GetSettingAsync("ChromeHidden"), "1", StringComparison.Ordinal);
        UpdateChromeVisibilityButtons();
        await RefreshTabsAsync();
        await RefreshMonitorsAsync();
        await RefreshHistoryAsync();
        UpdateDashboardSummary();
        UpdateActionStates();
        AppendActivity($"GPTDeskTop v{GetAppVersion()} ready.");
    }

    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        if (_selectedMonitor is null) _autoReplyBox.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        AppendActivity("Global monitoring, rotation and notification settings saved.");
    }

    private async Task LaunchChromeAsync()
    {
        try
        {
            _chrome.LaunchMonitorChrome();
            AppendActivity("Monitor Chrome launched.");
            await Task.Delay(1800);
            if (_chromeHidden) await _chrome.HideMonitorChromeAsync();
            await RefreshTabsAsync();
        }
        catch (Exception ex) { ShowError("Chrome Launch Error", ex.Message); }
    }

    private async Task HideChromeAsync()
    {
        try
        {
            if (!await _chrome.HideMonitorChromeAsync()) return;
            _chromeHidden = true;
            await _database.SetSettingAsync("ChromeHidden", "1");
            UpdateChromeVisibilityButtons();
            UpdateDashboardSummary();
            AppendActivity("Chrome hidden; monitoring continues.");
        }
        catch (Exception ex) { ShowError("Hide Chrome Error", ex.Message); }
    }

    private async Task ShowChromeAsync()
    {
        try
        {
            if (!await _chrome.ShowMonitorChromeAsync()) return;
            _chromeHidden = false;
            await _database.SetSettingAsync("ChromeHidden", "0");
            UpdateChromeVisibilityButtons();
            UpdateDashboardSummary();
            AppendActivity("Chrome shown.");
        }
        catch (Exception ex) { ShowError("Show Chrome Error", ex.Message); }
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
            AppendActivity($"Open Chrome tabs: {_tabs.Count}");
            if (_tabs.Count > 0)
            {
                _tabsGrid.ClearSelection();
                _tabsGrid.Rows[0].Selected = true;
                _tabsGrid.CurrentCell = _tabsGrid.Rows[0].Cells[0];
            }
            else
            {
                _selectedTab = null;
            }
            UpdateEmptyStates();
            UpdateDashboardSummary();
            UpdateActionStates();
        }
        catch (Exception ex)
        {
            _selectedTab = null;
            UpdateEmptyStates();
            UpdateDashboardSummary();
            UpdateActionStates();
            AppendActivity($"Cannot read Chrome tabs: {ex.Message}");
        }
    }

    private void SelectCurrentTab()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab)
        {
            _selectedTab = null;
            UpdateActionStates();
            return;
        }
        _selectedTab = tab;
        if (_selectedMonitor is null) _editorLabel.Text = $"Open tab selected: {tab.Title}";
        UpdateActionStates();
    }

    private void SelectCurrentMonitor()
    {
        if (_monitorsGrid.CurrentRow?.DataBoundItem is not SavedMonitor monitor)
        {
            _selectedMonitor = null;
            _autoReplyBox.Text = string.Empty;
            _enabledCheck.Checked = false;
            _editorLabel.Text = "Select a monitor to review its runtime settings.";
            UpdateActionStates();
            return;
        }

        _selectedMonitor = monitor;
        _autoReplyBox.Text = monitor.AutoReply;
        _enabledCheck.Checked = monitor.Enabled;
        _editorLabel.Text = $"#{monitor.Id} • {monitor.Title} • Delay {monitor.ReplyDelaySeconds}s • Poll {monitor.TimerSeconds}s • Rotations {monitor.RotationCount}";
        UpdateActionStates();
    }

    private async Task AddSelectedTabAsync()
    {
        var selectedTabs = _tabsGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ChromeTab>().DistinctBy(t => t.Id).ToList();
        if (selectedTabs.Count == 0 && _selectedTab is not null) selectedTabs.Add(_selectedTab);
        if (selectedTabs.Count == 0)
        {
            MessageBox.Show(this, "Select one or more Chrome tabs first.", "Add Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var defaultReply = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        var defaultDelay = await _database.GetIntSettingAsync("DefaultMonitorDelaySeconds", 3, 0, 300);
        var defaultTimer = await _database.GetIntSettingAsync("DefaultMonitorTimerSeconds", 1, 1, 60);
        long? lastId = null;
        foreach (var tab in selectedTabs)
        {
            var duplicate = _monitors.FirstOrDefault(m => string.Equals(m.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                lastId = duplicate.Id;
                AppendActivity($"Monitor #{duplicate.Id} already tracks: {tab.Title}");
                continue;
            }
            var monitor = new SavedMonitor { TabId = tab.Id, Title = tab.Title, Url = tab.Url, AutoReply = defaultReply, ReplyDelaySeconds = defaultDelay, TimerSeconds = defaultTimer, Enabled = true };
            if (!MonitorSettingsForm.Edit(this, monitor)) continue;
            await _database.SaveMonitorAsync(monitor);
            lastId = monitor.Id;
            AppendActivity($"Added monitor #{monitor.Id}: Delay {monitor.ReplyDelaySeconds}s / Timer {monitor.TimerSeconds}s");
        }
        await RefreshMonitorsAsync();
        if (lastId.HasValue) SelectMonitorRow(lastId.Value);
    }

    private async Task EditSelectedMonitorSettingsAsync()
    {
        if (_selectedMonitor is null) return;
        if (_monitor.IsMonitorRunning(_selectedMonitor.Id))
        {
            MessageBox.Show(this, "Stop this monitor before changing its settings.", "Monitor Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!MonitorSettingsForm.Edit(this, _selectedMonitor)) return;
        await _database.SaveMonitorAsync(_selectedMonitor);
        var id = _selectedMonitor.Id;
        await RefreshMonitorsAsync();
        SelectMonitorRow(id);
    }

    private async Task DeleteSelectedMonitorAsync()
    {
        if (_selectedMonitor is null) return;
        var monitor = _selectedMonitor;
        var message = $"Delete monitor #{monitor.Id}?{Environment.NewLine}{Environment.NewLine}{monitor.Title}{Environment.NewLine}{Environment.NewLine}The monitor will be stopped if necessary and its saved configuration will be removed. This cannot be undone.";
        if (MessageBox.Show(this, message, "Delete Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var id = monitor.Id;
        await _monitor.StopMonitorAsync(id);
        await _database.DeleteMonitorAsync(id);
        _selectedMonitor = null;
        await RefreshMonitorsAsync();
        AppendActivity($"Monitor #{id} deleted.");
    }

    private async Task StartSelectedMonitorAsync()
    {
        if (_selectedMonitor is not null) await StartMonitorAsync(_selectedMonitor);
    }

    private async Task StopSelectedMonitorAsync()
    {
        if (_selectedMonitor is null) return;
        await _monitor.StopMonitorAsync(_selectedMonitor.Id);
        await RefreshMonitorsAsync();
    }

    private async Task StopAllAsync()
    {
        await _monitor.StopAllAsync();
        await RefreshMonitorsAsync();
        AppendActivity("All monitors stopped.");
    }

    private async Task StartAllEnabledAsync()
    {
        await RefreshTabsAsync();
        foreach (var monitor in _monitors.Where(m => m.Enabled).ToList()) await StartMonitorAsync(monitor, false);
        await RefreshMonitorsAsync();
    }

    private async Task StartMonitorAsync(SavedMonitor monitor, bool refreshTabsIfMissing = true)
    {
        if (_monitor.IsMonitorRunning(monitor.Id)) return;
        var tab = ResolveTab(monitor);
        if (tab is null && refreshTabsIfMissing)
        {
            await RefreshTabsAsync();
            tab = ResolveTab(monitor);
        }
        if (tab is null)
        {
            AppendActivity($"Monitor #{monitor.Id}: matching tab not open.");
            return;
        }
        monitor.TabId = tab.Id;
        monitor.Title = tab.Title;
        monitor.Url = tab.Url;
        await _database.SaveMonitorAsync(monitor);
        await _monitor.StartMonitorAsync(monitor, tab);
        await RefreshMonitorsAsync();
    }

    private ChromeTab? ResolveTab(SavedMonitor monitor)
        => _tabs.FirstOrDefault(t => t.Id == monitor.TabId)
           ?? _tabs.FirstOrDefault(t => string.Equals(t.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));

    private async Task CloseSelectedTabAsync()
    {
        if (_tabsGrid.CurrentRow?.DataBoundItem is not ChromeTab tab) return;
        var message = $"Close this Chrome tab?{Environment.NewLine}{Environment.NewLine}{tab.Title}{Environment.NewLine}{Environment.NewLine}Any unsent text in this tab will be lost.";
        if (MessageBox.Show(this, message, "Close Chrome Tab", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await _chrome.CloseTabAsync(tab);
        AppendActivity($"Closed Chrome tab: {tab.Title}");
        await RefreshTabsAsync();
    }

    private async Task RefreshMonitorsAsync(bool preserveSelection = true)
    {
        var selectedId = preserveSelection ? _selectedMonitor?.Id : null;
        _monitors = await _database.GetSavedMonitorsAsync();
        foreach (var monitor in _monitors) monitor.RuntimeStatus = _monitor.IsMonitorRunning(monitor.Id) ? "Running" : "Stopped";
        _monitorsGrid.DataSource = null;
        _monitorsGrid.DataSource = _monitors;

        if (selectedId.HasValue)
        {
            SelectMonitorRow(selectedId.Value);
        }
        else if (_monitors.Count > 0)
        {
            _monitorsGrid.ClearSelection();
            _monitorsGrid.Rows[0].Selected = true;
            _monitorsGrid.CurrentCell = _monitorsGrid.Rows[0].Cells[0];
            SelectCurrentMonitor();
        }
        else
        {
            _selectedMonitor = null;
            _autoReplyBox.Text = string.Empty;
            _enabledCheck.Checked = false;
            _editorLabel.Text = "No saved monitors yet. Select an open ChatGPT tab and choose Add Monitor.";
        }

        UpdateEmptyStates();
        UpdateDashboardSummary();
        UpdateActionStates();
    }

    private void SelectMonitorRow(long id)
    {
        foreach (DataGridViewRow row in _monitorsGrid.Rows)
        {
            if (row.DataBoundItem is not SavedMonitor monitor || monitor.Id != id) continue;
            _monitorsGrid.ClearSelection();
            row.Selected = true;
            _monitorsGrid.CurrentCell = row.Cells[0];
            _selectedMonitor = monitor;
            SelectCurrentMonitor();
            return;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            _historyGrid.DataSource = null;
            _historyGrid.DataSource = await _database.GetRecentLogsAsync();
            UpdateEmptyStates();
        }
        catch (Exception ex)
        {
            UpdateEmptyStates();
            AppendActivity($"History error: {ex.Message}");
        }
    }

    private async Task DeleteSelectedLogAsync()
    {
        if (_historyGrid.CurrentRow?.DataBoundItem is not MessageLog log) return;
        var message = $"Delete this stored history entry?{Environment.NewLine}{Environment.NewLine}{log.Timestamp:yyyy-MM-dd HH:mm:ss}  •  {log.TabTitle}{Environment.NewLine}{log.Direction}  •  {log.Status}{Environment.NewLine}{Environment.NewLine}This cannot be undone.";
        if (MessageBox.Show(this, message, "Delete History Entry", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await _database.DeleteLogAsync(log.Id);
        await RefreshHistoryAsync();
    }

    private async Task ClearHistoryAsync()
    {
        var count = _historyGrid.Rows.Count;
        if (count == 0) return;
        var message = $"Delete all stored history?{Environment.NewLine}{Environment.NewLine}{count} visible entr{(count == 1 ? "y" : "ies")} will be removed. This cannot be undone.";
        if (MessageBox.Show(this, message, "Clear All History", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await _database.ClearLogsAsync();
        await RefreshHistoryAsync();
    }

    private void UpdateDashboardSummary()
    {
        var running = _monitors.Count(m => _monitor.IsMonitorRunning(m.Id));
        _chromeMetricValue.Text = _chromeHidden ? "Hidden" : "Visible";
        _chromeMetricValue.ForeColor = _chromeHidden ? FluentTheme.Warning : FluentTheme.Success;
        _tabsMetricValue.Text = _tabs.Count.ToString();
        _tabsMetricValue.ForeColor = _tabs.Count > 0 ? FluentTheme.Success : FluentTheme.Muted;
        _monitorsMetricValue.Text = _monitors.Count.ToString();
        _monitorsMetricValue.ForeColor = _monitors.Count > 0 ? FluentTheme.Accent : FluentTheme.Muted;
        _runningMetricValue.Text = running.ToString();
        _runningMetricValue.ForeColor = running > 0 ? FluentTheme.Success : FluentTheme.Muted;
    }

    private void UpdateActionStates()
    {
        var hasTab = _selectedTab is not null;
        var hasMonitor = _selectedMonitor is not null;
        var selectedRunning = hasMonitor && _monitor.IsMonitorRunning(_selectedMonitor!.Id);

        _addMonitorButton.Enabled = hasTab;
        _monitorSettingsButton.Enabled = hasMonitor && !selectedRunning;
        _quickMonitorSettingsButton.Enabled = hasMonitor && !selectedRunning;
        _deleteMonitorButton.Enabled = hasMonitor;
        _startSelectedButton.Enabled = hasMonitor && !selectedRunning;
        _stopSelectedButton.Enabled = selectedRunning;
        _startAllButton.Enabled = _monitors.Any(m => m.Enabled && !_monitor.IsMonitorRunning(m.Id));
        _stopAllButton.Enabled = _monitor.IsRunning;
        _settingsButton.Enabled = true;
        UpdateDashboardSummary();
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
        if (_shutdownRequested || IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownCompleted)
        {
            _toolTip.Dispose();
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownRequested) return;

        _shutdownRequested = true;
        ControlBox = false;
        Enabled = false;
        UseWaitCursor = true;
        Text = $"GPTDeskTop v{GetAppVersion()} - Closing...";
        _ = CompleteShutdownAsync();
    }

        private async Task CompleteShutdownAsync()
    {
        using (var layoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try
            {
                AppendActivity("Closing application: saving workspace layout...");
                await PersistOperatorLayoutAsync(layoutTimeout.Token);
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "MainForm.PersistOperatorLayout");
            }
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            AppendActivity("Closing application: stopping monitor workers...");
            if (_monitor.IsRunning)
                await _monitor.StopAllAsync().WaitAsync(timeout.Token);

            AppendActivity("Closing application: closing monitor Chrome tabs...");
            await _chrome.CloseAllMonitorTabsAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            AppendActivity("Shutdown cleanup reached the 10-second safety timeout. Continuing application exit.");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "MainForm.GracefulShutdown");
            AppendActivity($"Shutdown cleanup warning: {ex.Message}");
        }
        finally
        {
            _shutdownCompleted = true;
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(Close)); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
