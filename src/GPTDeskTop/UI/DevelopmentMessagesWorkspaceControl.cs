using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

/// <summary>
/// Premium single-surface workspace over the existing DevelopmentTaskEngine lifecycle.
/// It does not create a second delivery path: every action delegates to the canonical runtime binding.
/// </summary>
public sealed class DevelopmentMessagesWorkspaceControl : UserControl
{
    private readonly DevelopmentTaskRuntimeBinding _binding;
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
    private readonly Label _phase = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.MutedStrong, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _progress = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _evidence = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _lastError = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _countdown = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = FluentTheme.Text, Font = new Font("Cascadia Mono", 9.5F, FontStyle.Bold) };
    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _resume = new() { Text = "Resume", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly DataGridView _receipts = new();
    private readonly DevelopmentMessageCatalogControl _catalog;
    private readonly DevelopmentTaskScheduleSettingsControl _schedule;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public DevelopmentMessagesWorkspaceControl(
        DevelopmentTaskRuntimeBinding binding,
        string? catalogPath = null,
        string? scheduleSettingsPath = null)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _catalog = new DevelopmentMessageCatalogControl(catalogPath);
        _schedule = new DevelopmentTaskScheduleSettingsControl(scheduleSettingsPath);

        Name = "PremiumDevelopmentMessagesWorkspace";
        AccessibleName = "Development Messages workspace";
        AccessibleDescription = "Canonical development task lifecycle, message catalog, schedule and delivery evidence.";
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Padding = new Padding(14);

        ConfigureReceipts();
        BuildUi();
        WireEvents();
        RenderState();
    }

    private void ConfigureReceipts()
    {
        _receipts.Dock = DockStyle.Fill;
        _receipts.ReadOnly = true;
        _receipts.AllowUserToAddRows = false;
        _receipts.AllowUserToDeleteRows = false;
        _receipts.AllowUserToResizeRows = false;
        _receipts.MultiSelect = false;
        _receipts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _receipts.RowHeadersVisible = false;
        _receipts.AutoGenerateColumns = false;
        _receipts.AccessibleName = "Development delivery receipts";
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Delivered", Width = 142 });
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Message", Width = 70 });
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Monitor", Width = 105 });
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chat / Tab", Width = 120 });
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rev", Width = 58 });
        _receipts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fingerprint", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 120 });
        FluentTheme.StyleGrid(_receipts);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = FluentTheme.Background,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = FluentTheme.Background, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        header.Controls.Add(new Label
        {
            Text = "Development Messages",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(_countdown, 1, 0);
        var subtitle = FluentTheme.CreateMutedLabel("Real message lifecycle, delivery receipts, scheduling and recovery evidence from the canonical DevelopmentTaskEngine.");
        header.Controls.Add(subtitle, 0, 1);
        header.SetColumnSpan(subtitle, 2);
        root.Controls.Add(header, 0, 0);

        var summary = CreateCard();
        var summaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = FluentTheme.Surface, Padding = new Padding(10, 6, 10, 6) };
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
        summaryLayout.Controls.Add(_status, 0, 0);
        summaryLayout.SetRowSpan(_status, 2);
        summaryLayout.Controls.Add(_phase, 1, 0);
        summaryLayout.Controls.Add(_progress, 2, 0);
        summaryLayout.Controls.Add(_evidence, 3, 0);
        summaryLayout.Controls.Add(_lastError, 1, 1);
        summaryLayout.SetColumnSpan(_lastError, 3);
        summary.Controls.Add(summaryLayout);
        root.Controls.Add(summary, 0, 1);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = FluentTheme.Background,
            Panel1MinSize = 360,
            Panel2MinSize = 300,
            SplitterDistance = 650,
            Margin = new Padding(0, 10, 0, 8)
        };
        body.Panel1.Padding = new Padding(0, 0, 5, 0);
        body.Panel2.Padding = new Padding(5, 0, 0, 0);
        body.Panel1.Controls.Add(CreateSectionCard("Message catalog", "Edit the persisted message variants consumed by the current runtime.", _catalog));

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Background, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(CreateSectionCard("Schedule", "Work and cooling windows apply through the existing schedule store.", _schedule), 0, 0);
        right.Controls.Add(CreateSectionCard("Delivery receipts / evidence", "Verified deliveries from persisted DevelopmentTaskState receipts.", _receipts), 0, 1);
        body.Panel2.Controls.Add(right);
        root.Controls.Add(body, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = FluentTheme.Background,
            Padding = new Padding(0, 5, 0, 0)
        };
        actions.Controls.AddRange(new Control[] { _start, _pause, _resume, _stop });
        root.Controls.Add(actions, 0, 3);

        FluentTheme.StyleButton(_start, primary: true);
        FluentTheme.StyleButton(_pause);
        FluentTheme.StyleButton(_resume);
        FluentTheme.StyleButton(_stop, danger: true);
        Controls.Add(root);
    }

    private static Panel CreateCard()
        => new() { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(1), Margin = new Padding(0, 4, 0, 4) };

    private static Control CreateSectionCard(string title, string subtitle, Control body)
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = FluentTheme.Surface, Padding = new Padding(10), Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(FluentTheme.CreateSectionTitle(title), 0, 0);
        layout.Controls.Add(FluentTheme.CreateMutedLabel(subtitle), 0, 1);
        body.Margin = Padding.Empty;
        layout.Controls.Add(body, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private void WireEvents()
    {
        _start.Click += async (_, _) => await RunAsync(() => _binding.StartAsync("default-development-plan", "Development Plan"));
        _pause.Click += async (_, _) => await RunAsync(() => _binding.PauseAsync());
        _resume.Click += async (_, _) => await RunAsync(() => _binding.ResumeAsync());
        _stop.Click += async (_, _) => await RunAsync(() => _binding.StopAsync());
        _binding.Engine.StateChanged += OnStateChanged;
        _binding.Engine.MessageReady += OnMessageReady;
        _binding.Engine.CoolingStarted += OnCoolingChanged;
        _binding.Engine.CoolingCompleted += OnCoolingChanged;
        _timer.Tick += (_, _) => RenderState();
        VisibleChanged += (_, _) => UpdateTimer();
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Development Messages", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { RenderState(); }
    }

    private void OnStateChanged(object? sender, DevelopmentTaskState state) => Ui(RenderState);
    private void OnMessageReady(string message) => Ui(RenderState);
    private void OnCoolingChanged(object? sender, EventArgs e) => Ui(RenderState);

    private void RenderState()
    {
        if (IsDisposed || Disposing) return;
        var state = _binding.State;
        _status.Text = state.Status.ToString().ToUpperInvariant();
        ApplyStatusStyle(state.Status);
        _phase.Text = state.Status switch
        {
            DevelopmentTaskEngineStatus.Working => "Working — delivery enabled",
            DevelopmentTaskEngineStatus.Cooling => "Cooling — delivery suspended",
            DevelopmentTaskEngineStatus.Paused => "Paused — checkpoint preserved",
            DevelopmentTaskEngineStatus.Faulted => "Faulted — inspect error evidence",
            DevelopmentTaskEngineStatus.Completed => "Completed — all configured messages delivered",
            _ => "Stopped — no development delivery"
        };
        _progress.Text = $"Message {Math.Min(state.CurrentMessageIndex + 1, Math.Max(1, state.TotalMessages))} / {Math.Max(0, state.TotalMessages)}  •  Completed {state.CompletedMessages}";
        _evidence.Text = $"Receipts {state.DeliveryReceipts.Count}  •  Revision {state.Revision}  •  Last Chat {state.LastMonitorId ?? "—"}";
        _lastError.Text = string.IsNullOrWhiteSpace(state.LastError)
            ? $"Last checkpoint: {(state.LastCheckpointAt?.ToLocalTime().ToString("G") ?? "—")}"
            : $"Last error: {state.LastError}";
        _lastError.ForeColor = string.IsNullOrWhiteSpace(state.LastError) ? FluentTheme.Muted : FluentTheme.Danger;

        var remaining = Remaining(state);
        _countdown.Text = remaining > TimeSpan.Zero ? $"Next transition  {remaining:mm\\:ss}" : "Runtime ready";
        RenderReceipts(state);

        _start.Enabled = state.Status is DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Paused;
        _pause.Enabled = state.Status == DevelopmentTaskEngineStatus.Working;
        _resume.Enabled = state.Status is DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Stopped;
        _stop.Enabled = state.Status is not DevelopmentTaskEngineStatus.Stopped and not DevelopmentTaskEngineStatus.Completed;
        UpdateTimer();
    }

    private TimeSpan Remaining(DevelopmentTaskState state)
    {
        var now = DateTimeOffset.UtcNow;
        var value = state.Status == DevelopmentTaskEngineStatus.Working && state.WorkWindowStartedAt.HasValue
            ? _binding.Engine.WorkWindow - (now - state.WorkWindowStartedAt.Value)
            : state.Status == DevelopmentTaskEngineStatus.Cooling && state.CoolingStartedAt.HasValue
                ? _binding.Engine.CoolingWindow - (now - state.CoolingStartedAt.Value)
                : TimeSpan.Zero;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private void RenderReceipts(DevelopmentTaskState state)
    {
        _receipts.Rows.Clear();
        foreach (var receipt in state.DeliveryReceipts.Values.OrderByDescending(x => x.DeliveredAt).Take(250))
        {
            _receipts.Rows.Add(
                receipt.DeliveredAt.ToLocalTime().ToString("G"),
                receipt.MessageIndex + 1,
                string.IsNullOrWhiteSpace(receipt.MonitorId) ? "—" : receipt.MonitorId,
                string.IsNullOrWhiteSpace(receipt.TabId) ? "—" : receipt.TabId,
                receipt.Revision,
                string.IsNullOrWhiteSpace(receipt.Fingerprint) ? "—" : receipt.Fingerprint);
        }
    }

    private void ApplyStatusStyle(DevelopmentTaskEngineStatus status)
    {
        (_status.ForeColor, _status.BackColor) = status switch
        {
            DevelopmentTaskEngineStatus.Working => (FluentTheme.Success, FluentTheme.SuccessSubtle),
            DevelopmentTaskEngineStatus.Cooling or DevelopmentTaskEngineStatus.Paused => (FluentTheme.Warning, FluentTheme.WarningSubtle),
            DevelopmentTaskEngineStatus.Faulted => (FluentTheme.Danger, FluentTheme.DangerSubtle),
            DevelopmentTaskEngineStatus.Completed => (FluentTheme.Accent, FluentTheme.AccentSubtle),
            _ => (FluentTheme.MutedStrong, FluentTheme.SurfaceAlt)
        };
    }

    private void UpdateTimer()
    {
        var shouldRun = Visible && _binding.State.Status is DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling;
        if (shouldRun && !_timer.Enabled) _timer.Start();
        else if (!shouldRun && _timer.Enabled) _timer.Stop();
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
            _timer.Stop();
            _binding.Engine.StateChanged -= OnStateChanged;
            _binding.Engine.MessageReady -= OnMessageReady;
            _binding.Engine.CoolingStarted -= OnCoolingChanged;
            _binding.Engine.CoolingCompleted -= OnCoolingChanged;
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
