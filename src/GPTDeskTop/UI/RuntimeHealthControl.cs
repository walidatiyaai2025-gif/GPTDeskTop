using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class RuntimeHealthControl : UserControl
{
    private const int CollapsedHeight = 62;
    private const int ExpandedHeight = 188;

    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;

    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Bold),
        Text = "● NOT CHECKED",
        TextAlign = ContentAlignment.MiddleCenter,
        AccessibleRole = AccessibleRole.StatusBar
    };
    private readonly Label _summaryLabel = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = FluentTheme.Muted,
        Text = "Runtime health check pending.",
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label _lastCheckedLabel = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = FluentTheme.Muted,
        Text = "Not checked",
        TextAlign = ContentAlignment.MiddleRight,
        AutoEllipsis = true
    };
    private readonly Label _chromeValue = CreateMetricValue("Unknown");
    private readonly Label _databaseValue = CreateMetricValue("Unknown");
    private readonly Label _tabsValue = CreateMetricValue("—");
    private readonly Label _monitorsValue = CreateMetricValue("—");
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _toggleButton = new() { Text = "Details", AutoSize = true };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 9000, InitialDelay = 400, ReshowDelay = 100 };

    private List<SavedMonitor> _savedMonitors = new();
    private bool _expanded;
    private bool _loading;

    public event EventHandler? ExpandedChanged;

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            ApplyExpandedState();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public RuntimeHealthControl(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _database = database ?? throw new ArgumentNullException(nameof(database));

        Dock = DockStyle.Top;
        Height = CollapsedHeight;
        MinimumSize = new Size(0, CollapsedHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Padding = new Padding(12, 4, 12, 4);
        AccessibleName = "Runtime health and connection center";
        AccessibleDescription = "Shows Chrome DevTools, SQLite, ChatGPT tab and saved monitor health without changing runtime state.";

        BuildUi();
        ConfigureAccessibility();
        ConfigureTooltips();
        WireEvents();
        ApplyExpandedState();

        Load += async (_, _) => await RefreshAsync();
    }

    private void BuildUi()
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 5, 10, 7)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));

        header.Controls.Add(new Label
        {
            Text = "Runtime Health",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(_statusLabel, 1, 0);
        header.Controls.Add(_summaryLabel, 2, 0);
        header.Controls.Add(_lastCheckedLabel, 3, 0);
        header.Controls.Add(_refreshButton, 4, 0);
        header.Controls.Add(_toggleButton, 5, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 8, 0, 0)
        };
        for (var i = 0; i < 4; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        metrics.Controls.Add(CreateMetricCard("Chrome / CDP", _chromeValue), 0, 0);
        metrics.Controls.Add(CreateMetricCard("SQLite", _databaseValue), 1, 0);
        metrics.Controls.Add(CreateMetricCard("ChatGPT Tabs", _tabsValue), 2, 0);
        metrics.Controls.Add(CreateMetricCard("Saved / Running", _monitorsValue), 3, 0);
        _body.Controls.Add(metrics);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_body, 0, 1);
        frame.Controls.Add(root);
        Controls.Add(frame);

        FluentTheme.StyleButton(_refreshButton, primary: true);
        FluentTheme.StyleButton(_toggleButton);
    }

    private static Control CreateMetricCard(string caption, Label value)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.SurfaceAlt,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(4),
            Padding = new Padding(12, 8, 12, 8)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.SurfaceAlt
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        layout.Controls.Add(value, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static Label CreateMetricValue(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };

    private void ConfigureAccessibility()
    {
        _statusLabel.AccessibleName = "Overall runtime health";
        _summaryLabel.AccessibleName = "Runtime health summary";
        _lastCheckedLabel.AccessibleName = "Last runtime health refresh";
        _refreshButton.AccessibleName = "Refresh runtime health";
        _refreshButton.AccessibleDescription = "Run read-only Chrome DevTools and SQLite health probes.";
        _toggleButton.AccessibleName = "Expand or collapse runtime health details";
        _chromeValue.AccessibleName = "Chrome DevTools health";
        _databaseValue.AccessibleName = "SQLite health";
        _tabsValue.AccessibleName = "Open ChatGPT tab count";
        _monitorsValue.AccessibleName = "Saved and running monitor count";
        _refreshButton.TabIndex = 0;
        _toggleButton.TabIndex = 1;
    }

    private void ConfigureTooltips()
    {
        _toolTip.SetToolTip(_refreshButton, "Refresh Chrome/CDP, SQLite, tab and monitor health. F5 also refreshes while this panel has focus.");
        _toolTip.SetToolTip(_toggleButton, "Show or hide runtime health details.");
        _toolTip.SetToolTip(_chromeValue, "Read-only Chrome DevTools /json/list reachability probe.");
        _toolTip.SetToolTip(_databaseValue, "Read-only SavedMonitors query against the configured SQLite database.");
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _toggleButton.Click += (_, _) => IsExpanded = !IsExpanded;
        _monitor.RunningStateChanged += OnRunningStateChanged;
    }

    private async Task RefreshAsync()
    {
        if (_loading || IsDisposed || Disposing) return;

        _loading = true;
        _refreshButton.Enabled = false;
        _statusLabel.Text = "● CHECKING";
        _statusLabel.ForeColor = FluentTheme.Accent;
        _statusLabel.BackColor = FluentTheme.AccentSubtle;
        _summaryLabel.Text = "Checking runtime dependencies…";
        _summaryLabel.ForeColor = FluentTheme.Accent;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var chromeTask = ProbeChromeAsync(timeout.Token);
            var databaseTask = ProbeDatabaseAsync(timeout.Token);
            await Task.WhenAll(chromeTask, databaseTask);

            var chromeProbe = await chromeTask;
            var databaseProbe = await databaseTask;
            _savedMonitors = databaseProbe.Value ?? new List<SavedMonitor>();

            var tabs = chromeProbe.Value ?? new List<ChromeTab>();
            var chatGptTabs = tabs.Count(tab => RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url));
            var runningMonitors = _savedMonitors.Count(monitor => _monitor.IsMonitorRunning(monitor.Id));
            var snapshot = RuntimeHealthPresentation.Create(
                chromeProbe.Succeeded,
                databaseProbe.Succeeded,
                chatGptTabs,
                _savedMonitors.Count,
                runningMonitors,
                DateTimeOffset.Now,
                chromeProbe.Error,
                databaseProbe.Error);

            Render(snapshot);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "RuntimeHealthControl.Refresh");
            _statusLabel.Text = "● UNAVAILABLE";
            _statusLabel.ForeColor = FluentTheme.Danger;
            _statusLabel.BackColor = FluentTheme.DangerSubtle;
            _summaryLabel.Text = $"Health refresh failed: {ex.Message}";
            _summaryLabel.ForeColor = FluentTheme.Danger;
            _lastCheckedLabel.Text = $"Failed {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            _loading = false;
            _refreshButton.Enabled = true;
        }
    }

    private async Task<ProbeResult<List<ChromeTab>>> ProbeChromeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ProbeResult<List<ChromeTab>>(true, await _chrome.GetTabsAsync(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult<List<ChromeTab>>(false, null, "Health probe timed out.");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "RuntimeHealthControl.ChromeProbe");
            return new ProbeResult<List<ChromeTab>>(false, null, ex.Message);
        }
    }

    private async Task<ProbeResult<List<SavedMonitor>>> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ProbeResult<List<SavedMonitor>>(true, await _database.GetSavedMonitorsAsync(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult<List<SavedMonitor>>(false, null, "Health probe timed out.");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "RuntimeHealthControl.DatabaseProbe");
            return new ProbeResult<List<SavedMonitor>>(false, null, ex.Message);
        }
    }

    private void Render(RuntimeHealthSnapshot snapshot)
    {
        _statusLabel.Text = $"● {snapshot.Level.ToString().ToUpperInvariant()}";
        (_statusLabel.ForeColor, _statusLabel.BackColor) = snapshot.Level switch
        {
            RuntimeHealthLevel.Healthy => (FluentTheme.Success, FluentTheme.SuccessSubtle),
            RuntimeHealthLevel.Degraded => (FluentTheme.Warning, FluentTheme.WarningSubtle),
            _ => (FluentTheme.Danger, FluentTheme.DangerSubtle)
        };

        _summaryLabel.Text = snapshot.Summary;
        _summaryLabel.ForeColor = snapshot.Level switch
        {
            RuntimeHealthLevel.Healthy => FluentTheme.Muted,
            RuntimeHealthLevel.Degraded => FluentTheme.Warning,
            _ => FluentTheme.Danger
        };
        _lastCheckedLabel.Text = $"Checked {snapshot.CheckedAt:HH:mm:ss}";

        SetMetric(
            _chromeValue,
            snapshot.ChromeReachable ? "Reachable" : "Unavailable",
            snapshot.ChromeReachable ? FluentTheme.Success : FluentTheme.Danger,
            snapshot.ChromeError);
        SetMetric(
            _databaseValue,
            snapshot.DatabaseReachable ? "Reachable" : "Unavailable",
            snapshot.DatabaseReachable ? FluentTheme.Success : FluentTheme.Danger,
            snapshot.DatabaseError);
        SetMetric(
            _tabsValue,
            snapshot.ChromeReachable ? snapshot.ChatGptTabCount.ToString() : "—",
            snapshot.ChatGptTabCount > 0 ? FluentTheme.Success : FluentTheme.Muted,
            "Open ChatGPT conversation tabs visible through CDP.");
        SetMetric(
            _monitorsValue,
            snapshot.DatabaseReachable ? $"{snapshot.SavedMonitorCount} / {snapshot.RunningMonitorCount}" : "—",
            snapshot.RunningMonitorCount > 0 ? FluentTheme.Success : FluentTheme.Muted,
            "Saved monitors / currently running monitor workers.");
    }

    private void SetMetric(Label label, string text, Color color, string? tooltip)
    {
        label.Text = text;
        label.ForeColor = color;
        if (!string.IsNullOrWhiteSpace(tooltip)) _toolTip.SetToolTip(label, tooltip);
    }

    private void OnRunningStateChanged()
        => Ui(UpdateRunningMonitorMetric);

    private void UpdateRunningMonitorMetric()
    {
        if (_savedMonitors.Count == 0) return;
        var running = _savedMonitors.Count(monitor => _monitor.IsMonitorRunning(monitor.Id));
        _monitorsValue.Text = $"{_savedMonitors.Count} / {running}";
        _monitorsValue.ForeColor = running > 0 ? FluentTheme.Success : FluentTheme.Muted;
    }

    private void ApplyExpandedState()
    {
        _body.Visible = _expanded;
        _toggleButton.Text = _expanded ? "Collapse" : "Details";
        Height = _expanded ? ExpandedHeight : CollapsedHeight;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ContainsFocus && keyData == Keys.F5)
        {
            _ = RefreshAsync();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.RunningStateChanged -= OnRunningStateChanged;
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record ProbeResult<T>(bool Succeeded, T? Value, string? Error) where T : class;
}
