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
    private readonly Label _recoveryValue = CreateMetricValue("Unknown");
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _repairButton = new() { Text = "Repair…", AutoSize = true, Enabled = false };
    private readonly Button _retryRecoveryButton = new() { Text = "Retry", AutoSize = true, Enabled = false };
    private readonly Button _toggleButton = new() { Text = "Details", AutoSize = true };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 9000, InitialDelay = 400, ReshowDelay = 100 };

    private List<SavedMonitor> _savedMonitors = new();
    private bool _expanded;
    private bool _loading;
    private bool _recoveryRetrying;

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
        AccessibleDescription = "Shows Chrome DevTools, SQLite, ChatGPT conversation, saved monitor, duplicate ownership and crash recovery health without changing runtime state during health probes.";

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
            ColumnCount = 8,
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
        header.Controls.Add(_repairButton, 5, 0);
        header.Controls.Add(_retryRecoveryButton, 6, 0);
        header.Controls.Add(_toggleButton, 7, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 8, 0, 0)
        };
        for (var i = 0; i < 5; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        metrics.Controls.Add(CreateMetricCard("Chrome / CDP", _chromeValue), 0, 0);
        metrics.Controls.Add(CreateMetricCard("SQLite", _databaseValue), 1, 0);
        metrics.Controls.Add(CreateMetricCard("Conversations", _tabsValue), 2, 0);
        metrics.Controls.Add(CreateMetricCard("Saved / Running", _monitorsValue), 3, 0);
        metrics.Controls.Add(CreateMetricCard("Recovery", _recoveryValue), 4, 0);
        _body.Controls.Add(metrics);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_body, 0, 1);
        frame.Controls.Add(root);
        Controls.Add(frame);

        FluentTheme.StyleButton(_refreshButton, primary: true);
        FluentTheme.StyleButton(_repairButton);
        FluentTheme.StyleButton(_retryRecoveryButton);
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
        _repairButton.AccessibleName = "Repair recovery blocker";
        _repairButton.AccessibleDescription = "Safely rebind an invalid legacy saved monitor to an open stable ChatGPT conversation.";
        _retryRecoveryButton.AccessibleName = "Retry pending crash recovery";
        _retryRecoveryButton.AccessibleDescription = "Run a non-destructive pending recovery retry after all conversation identity blockers are repaired.";
        _toggleButton.AccessibleName = "Expand or collapse runtime health details";
        _chromeValue.AccessibleName = "Chrome DevTools health";
        _databaseValue.AccessibleName = "SQLite health";
        _tabsValue.AccessibleName = "Open ChatGPT conversation count";
        _monitorsValue.AccessibleName = "Saved and running monitor count";
        _recoveryValue.AccessibleName = "Crash recovery health";
        _refreshButton.TabIndex = 0;
        _repairButton.TabIndex = 1;
        _retryRecoveryButton.TabIndex = 2;
        _toggleButton.TabIndex = 3;
    }

    private void ConfigureTooltips()
    {
        _toolTip.SetToolTip(_refreshButton, "Refresh Chrome/CDP, SQLite, conversation, monitor and recovery health. F5 also refreshes while this panel has focus.");
        _toolTip.SetToolTip(_repairButton, "Repair an invalid saved monitor identity without deleting its Monitor ID, history or settings.");
        _toolTip.SetToolTip(_retryRecoveryButton, "Retry unresolved crash recovery in this session using PendingRetry. Already-verified receipts are not resent.");
        _toolTip.SetToolTip(_toggleButton, "Show or hide runtime health details.");
        _toolTip.SetToolTip(_chromeValue, "Read-only Chrome DevTools /json/list reachability probe.");
        _toolTip.SetToolTip(_databaseValue, "Read-only SavedMonitors query against the configured SQLite database.");
        _toolTip.SetToolTip(_recoveryValue, "Shows whether crash recovery is clear, pending, or blocked by invalid identities or duplicate conversation ownership.");
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        _repairButton.Click += async (_, _) => await RepairIdentityAsync();
        _retryRecoveryButton.Click += async (_, _) => await RetryPendingRecoveryAsync();
        _toggleButton.Click += (_, _) => IsExpanded = !IsExpanded;
        _monitor.RunningStateChanged += OnRunningStateChanged;
    }

    private async Task RefreshAsync()
    {
        if (_loading || IsDisposed || Disposing) return;

        _loading = true;
        _refreshButton.Enabled = false;
        _repairButton.Enabled = false;
        _retryRecoveryButton.Enabled = false;
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
            _savedMonitors = databaseProbe.Value?.Monitors ?? new List<SavedMonitor>();

            var tabs = chromeProbe.Value ?? new List<ChromeTab>();
            var conversationTabs = tabs.Count(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url));
            var runningMonitors = _savedMonitors.Count(monitor => _monitor.IsMonitorRunning(monitor.Id));
            var invalidMonitorCount = _savedMonitors.Count(monitor => !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url));
            var duplicateMonitorCount = MonitorConversationOwnership.CountDuplicateMonitors(_savedMonitors);
            var recoveryPending = databaseProbe.Value?.CrashRecoveryPending == true;
            var snapshot = RuntimeHealthPresentation.Create(
                chromeProbe.Succeeded,
                databaseProbe.Succeeded,
                conversationTabs,
                _savedMonitors.Count,
                runningMonitors,
                DateTimeOffset.Now,
                chromeProbe.Error,
                databaseProbe.Error,
                crashRecoveryPending: recoveryPending,
                invalidMonitorIdentityCount: invalidMonitorCount,
                duplicateMonitorOwnershipCount: duplicateMonitorCount);

            Render(snapshot);
            _repairButton.Enabled = chromeProbe.Succeeded
                && databaseProbe.Succeeded
                && (invalidMonitorCount > 0 || duplicateMonitorCount > 0);
            _retryRecoveryButton.Enabled = !_recoveryRetrying
                && chromeProbe.Succeeded
                && databaseProbe.Succeeded
                && recoveryPending
                && invalidMonitorCount == 0
                && duplicateMonitorCount == 0;
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
            _recoveryValue.Text = "Unknown";
            _recoveryValue.ForeColor = FluentTheme.Muted;
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

    private async Task<ProbeResult<DatabaseHealthData>> ProbeDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
            var pending = string.Equals(
                await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),
                "1",
                StringComparison.Ordinal);
            return new ProbeResult<DatabaseHealthData>(true, new DatabaseHealthData(monitors, pending), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult<DatabaseHealthData>(false, null, "Health probe timed out.");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "RuntimeHealthControl.DatabaseProbe");
            return new ProbeResult<DatabaseHealthData>(false, null, ex.Message);
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

        SetMetric(_chromeValue, snapshot.ChromeReachable ? "Reachable" : "Unavailable", snapshot.ChromeReachable ? FluentTheme.Success : FluentTheme.Danger, snapshot.ChromeError);
        SetMetric(_databaseValue, snapshot.DatabaseReachable ? "Reachable" : "Unavailable", snapshot.DatabaseReachable ? FluentTheme.Success : FluentTheme.Danger, snapshot.DatabaseError);
        SetMetric(_tabsValue, snapshot.ChromeReachable ? snapshot.ChatGptTabCount.ToString() : "—", snapshot.ChatGptTabCount > 0 ? FluentTheme.Success : FluentTheme.Muted, "Open stable ChatGPT conversations visible through CDP.");
        SetMetric(_monitorsValue, snapshot.DatabaseReachable ? $"{snapshot.SavedMonitorCount} / {snapshot.RunningMonitorCount}" : "—", snapshot.RunningMonitorCount > 0 ? FluentTheme.Success : FluentTheme.Muted, "Saved monitors / currently running monitor workers.");

        var hasRecoveryBlocker = snapshot.InvalidMonitorIdentityCount > 0 || snapshot.DuplicateMonitorOwnershipCount > 0;
        var recoveryText = snapshot.InvalidMonitorIdentityCount > 0
            ? $"Blocked ({snapshot.InvalidMonitorIdentityCount})"
            : snapshot.DuplicateMonitorOwnershipCount > 0
                ? $"Blocked (D{snapshot.DuplicateMonitorOwnershipCount})"
                : snapshot.CrashRecoveryPending ? "Pending" : "Clear";
        var recoveryColor = hasRecoveryBlocker || snapshot.CrashRecoveryPending
            ? FluentTheme.Warning
            : FluentTheme.Success;
        SetMetric(
            _recoveryValue,
            snapshot.DatabaseReachable ? recoveryText : "—",
            snapshot.DatabaseReachable ? recoveryColor : FluentTheme.Muted,
            hasRecoveryBlocker
                ? $"Invalid conversation identities: {snapshot.InvalidMonitorIdentityCount}. Duplicate conversation owners: {snapshot.DuplicateMonitorOwnershipCount}."
                : snapshot.CrashRecoveryPending
                    ? "Crash recovery still has unresolved work pending."
                    : "No pending crash recovery blocker is recorded.");
    }

    private async Task RepairIdentityAsync()
    {
        using var form = new MonitorIdentityRepairForm(_chrome, _database);
        if (form.ShowDialog(FindForm()) != DialogResult.OK) return;
        await RefreshAsync();
    }

    private async Task RetryPendingRecoveryAsync()
    {
        if (_recoveryRetrying) return;

        var pending = string.Equals(await _database.GetSettingAsync("CrashRecoveryPending"), "1", StringComparison.Ordinal);
        if (!pending)
        {
            await RefreshAsync();
            return;
        }

        var monitors = await _database.GetSavedMonitorsAsync();
        if (monitors.Any(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)))
        {
            MessageBox.Show(
                FindForm(),
                "Crash recovery is still blocked by an invalid saved monitor identity. Use Repair… first.",
                "Recovery Blocked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        var duplicateMonitorCount = MonitorConversationOwnership.CountDuplicateMonitors(monitors);
        if (duplicateMonitorCount > 0)
        {
            MessageBox.Show(
                FindForm(),
                $"Crash recovery is blocked by duplicate ownership on {duplicateMonitorCount} saved monitor row(s). Use Repair… to move a duplicate owner to an unowned stable conversation before retrying.",
                "Recovery Blocked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            await RefreshAsync();
            return;
        }

        var confirmation = "Retry pending crash recovery now?\n\nThis uses the non-destructive PendingRetry path. It may send the configured recovery message only to unresolved monitors; monitors with persisted success receipts are not sent again.";
        if (MessageBox.Show(FindForm(), confirmation, "Retry Crash Recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _recoveryRetrying = true;
        _refreshButton.Enabled = false;
        _repairButton.Enabled = false;
        _retryRecoveryButton.Enabled = false;
        _statusLabel.Text = "● RECOVERING";
        _statusLabel.ForeColor = FluentTheme.Accent;
        _statusLabel.BackColor = FluentTheme.AccentSubtle;
        _summaryLabel.Text = "Retrying unresolved crash recovery without global monitor/tab teardown…";
        _summaryLabel.ForeColor = FluentTheme.Accent;

        try
        {
            await CrashRecoveryService.RecoverIfPendingAsync(
                _chrome,
                _monitor,
                _database,
                CrashRecoveryMode.PendingRetry);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "RuntimeHealthControl.RetryPendingRecovery");
            MessageBox.Show(FindForm(), ex.Message, "Recovery Retry Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _recoveryRetrying = false;
            await RefreshAsync();
        }
    }

    private void SetMetric(Label label, string text, Color color, string? tooltip)
    {
        label.Text = text;
        label.ForeColor = color;
        if (!string.IsNullOrWhiteSpace(tooltip)) _toolTip.SetToolTip(label, tooltip);
    }

    private void OnRunningStateChanged() => Ui(UpdateRunningMonitorMetric);

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

    private sealed record DatabaseHealthData(List<SavedMonitor> Monitors, bool CrashRecoveryPending);
    private sealed record ProbeResult<T>(bool Succeeded, T? Value, string? Error) where T : class;
}