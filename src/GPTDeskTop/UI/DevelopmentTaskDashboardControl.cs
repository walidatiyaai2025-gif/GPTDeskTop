using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

public sealed class DevelopmentTaskDashboardControl : UserControl
{
    private const int CollapsedHeight = 72;
    private const int ExpandedHeight = 178;

    private readonly DevelopmentTaskRuntimeBinding _binding;
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(8, 0, 8, 0)
    };
    private readonly Label _phase = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = FluentTheme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label _message = new() { AutoSize = true, Margin = new Padding(0, 4, 22, 4) };
    private readonly Label _recipients = new() { AutoSize = true, Margin = new Padding(0, 4, 22, 4) };
    private readonly Label _delivery = new() { AutoSize = true, Margin = new Padding(0, 4, 22, 4) };
    private readonly Label _countdown = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Cascadia Mono", 9.5F, FontStyle.Bold),
        ForeColor = FluentTheme.Text,
        TextAlign = ContentAlignment.MiddleRight
    };
    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _resume = new() { Text = "Resume", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly Button _messagesButton = new() { Text = "Messages", AutoSize = true };
    private readonly Button _settingsButton = new() { Text = "Schedule", AutoSize = true };
    private readonly Button _toggle = new() { Text = "Collapse", AutoSize = true };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private bool _expanded = true;

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

    public DevelopmentTaskDashboardControl(DevelopmentTaskRuntimeBinding binding)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Dock = DockStyle.Top;
        Height = ExpandedHeight;
        MinimumSize = new Size(0, CollapsedHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        Padding = new Padding(12, 8, 12, 4);
        BuildUi();
        WireEvents();
        Render();
        _timer.Start();
    }

    private void BuildUi()
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 5, 10, 8)
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = FluentTheme.Surface,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

        header.Controls.Add(new Label
        {
            Text = "Development Plan",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
            ForeColor = FluentTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(_status, 1, 0);
        header.Controls.Add(_phase, 2, 0);
        header.Controls.Add(_countdown, 3, 0);
        header.Controls.Add(_toggle, 4, 0);

        var bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(0, 6, 0, 0)
        };
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var details = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(2, 2, 2, 0)
        };
        details.Controls.Add(_message);
        details.Controls.Add(_recipients);
        details.Controls.Add(_delivery);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = FluentTheme.Surface,
            Padding = Padding.Empty
        };
        buttons.Controls.AddRange(new Control[] { _start, _pause, _resume, _stop, _messagesButton, _settingsButton });

        bodyLayout.Controls.Add(details, 0, 0);
        bodyLayout.Controls.Add(buttons, 0, 1);
        _body.Controls.Add(bodyLayout);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_body, 0, 1);
        frame.Controls.Add(root);
        Controls.Add(frame);

        FluentTheme.StyleButton(_start, primary: true);
        FluentTheme.StyleButton(_stop, danger: true);
        FluentTheme.StyleButton(_settingsButton);
        FluentTheme.StyleButton(_toggle);
    }

    private void WireEvents()
    {
        _start.Click += async (_, _) => await RunAsync(() => _binding.StartAsync("default-development-plan", "Development Plan"));
        _pause.Click += async (_, _) => await RunAsync(() => _binding.PauseAsync());
        _resume.Click += async (_, _) => await RunAsync(() => _binding.ResumeAsync());
        _stop.Click += async (_, _) => await RunAsync(() => _binding.StopAsync());
        _messagesButton.Click += (_, _) => OpenMessageCatalog();
        _settingsButton.Click += (_, _) => OpenScheduleSettings();
        _toggle.Click += (_, _) => ToggleExpanded();
        _binding.Engine.StateChanged += OnStateChanged;
        _binding.Engine.MessageReady += OnMessageReady;
        _binding.Engine.CoolingStarted += OnCoolingChanged;
        _binding.Engine.CoolingCompleted += OnCoolingChanged;
        _timer.Tick += (_, _) => Render();
    }

    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private void ApplyExpandedState()
    {
        _body.Visible = _expanded;
        _toggle.Text = _expanded ? "Collapse" : "Details";
        Height = _expanded ? ExpandedHeight : CollapsedHeight;
    }

    private void OpenMessageCatalog()
    {
        using var dialog = new Form
        {
            Text = "Development Message Catalog",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(900, 600),
            Size = new Size(1100, 720),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        dialog.Controls.Add(new DevelopmentMessageCatalogControl { Dock = DockStyle.Fill });
        dialog.ShowDialog(FindForm());
    }

    private void OpenScheduleSettings()
    {
        using var dialog = new Form
        {
            Text = "Development Schedule",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(560, 260),
            Size = new Size(620, 300),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        dialog.Controls.Add(new DevelopmentTaskScheduleSettingsControl { Dock = DockStyle.Fill });
        dialog.ShowDialog(FindForm());
    }

    private void OnStateChanged(object? sender, DevelopmentTaskState e) => Ui(Render);
    private void OnMessageReady(string _) => Ui(Render);
    private void OnCoolingChanged(object? sender, EventArgs e) => Ui(Render);

    private async Task RunAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Development Plan", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Render(); }
    }

    private void Render()
    {
        if (IsDisposed) return;
        var state = _binding.State;
        _status.Text = $"● {state.Status}";
        ApplyStatusStyle(state.Status);
        _phase.Text = state.Status == DevelopmentTaskEngineStatus.Cooling
            ? "Cooling — no delivery"
            : state.Status == DevelopmentTaskEngineStatus.Working
                ? "Working — delivery enabled"
                : state.Status == DevelopmentTaskEngineStatus.Paused
                    ? "Paused — checkpoint preserved"
                    : state.Status.ToString();
        _message.Text = $"Message {state.CurrentMessageIndex + 1} / {Math.Max(0, state.TotalMessages)}";
        _recipients.Text = $"Last Chat {state.LastMonitorId ?? "—"}  •  Tab {state.LastTabId ?? "—"}";
        _delivery.Text = $"Verified {(state.LastDeliveredMessageIndex >= 0 ? (state.LastDeliveredMessageIndex + 1).ToString() : "—")}  •  Receipts {state.DeliveryReceipts.Count}  •  Rev {state.Revision}";

        var now = DateTimeOffset.UtcNow;
        var remaining = state.Status == DevelopmentTaskEngineStatus.Working && state.WorkWindowStartedAt.HasValue
            ? _binding.Engine.WorkWindow - (now - state.WorkWindowStartedAt.Value)
            : state.Status == DevelopmentTaskEngineStatus.Cooling && state.CoolingStartedAt.HasValue
                ? _binding.Engine.CoolingWindow - (now - state.CoolingStartedAt.Value)
                : TimeSpan.Zero;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        _countdown.Text = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "—";

        // Keep the established lifecycle action contract unchanged.
        _start.Enabled = state.Status is DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Paused;
        _pause.Enabled = state.Status == DevelopmentTaskEngineStatus.Working;
        _resume.Enabled = state.Status is DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Stopped;
        _stop.Enabled = state.Status is not DevelopmentTaskEngineStatus.Stopped and not DevelopmentTaskEngineStatus.Completed;
    }

    private void ApplyStatusStyle(DevelopmentTaskEngineStatus status)
    {
        (_status.ForeColor, _status.BackColor) = status switch
        {
            DevelopmentTaskEngineStatus.Working => (FluentTheme.Success, FluentTheme.SuccessSubtle),
            DevelopmentTaskEngineStatus.Cooling => (FluentTheme.Warning, FluentTheme.WarningSubtle),
            DevelopmentTaskEngineStatus.Paused => (FluentTheme.Warning, FluentTheme.WarningSubtle),
            DevelopmentTaskEngineStatus.Completed => (FluentTheme.Accent, FluentTheme.AccentSubtle),
            _ => (FluentTheme.Muted, FluentTheme.SurfaceAlt)
        };
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
            _binding.Engine.StateChanged -= OnStateChanged;
            _binding.Engine.MessageReady -= OnMessageReady;
            _binding.Engine.CoolingStarted -= OnCoolingChanged;
            _binding.Engine.CoolingCompleted -= OnCoolingChanged;
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}