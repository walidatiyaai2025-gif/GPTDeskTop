using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

public sealed class DevelopmentTaskDashboardControl : UserControl
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold) };
    private readonly Label _phase = new() { AutoSize = true };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label _recipients = new() { AutoSize = true };
    private readonly Label _delivery = new() { AutoSize = true };
    private readonly Label _countdown = new() { AutoSize = true, Font = new Font("Cascadia Mono", 10F, FontStyle.Bold) };
    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _resume = new() { Text = "Resume", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public DevelopmentTaskDashboardControl(DevelopmentTaskEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
        BackColor = FluentTheme.Surface;
        BuildUi();
        WireEvents();
        Render();
        _timer.Start();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label { Text = "Development Plan", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(_status, 1, 0);
        root.Controls.Add(new Label { Text = "Phase", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        root.Controls.Add(_phase, 3, 0);

        var details = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        details.Controls.Add(_message);
        details.Controls.Add(_recipients);
        details.Controls.Add(_delivery);
        details.Controls.Add(_countdown);
        root.Controls.Add(details, 0, 1); root.SetColumnSpan(details, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange(new Control[] { _start, _pause, _resume, _stop });
        root.Controls.Add(buttons, 0, 2); root.SetColumnSpan(buttons, 4);
        Controls.Add(root);
        FluentTheme.StyleButton(_start, primary: true);
        FluentTheme.StyleButton(_stop, danger: true);
    }

    private void WireEvents()
    {
        _start.Click += async (_, _) => await RunAsync(() => _engine.StartAsync("default-development-plan", "Development Plan"));
        _pause.Click += async (_, _) => await RunAsync(_engine.PauseAsync);
        _resume.Click += async (_, _) => await RunAsync(_engine.ResumeAsync);
        _stop.Click += async (_, _) => await RunAsync(_engine.StopAsync);
        _engine.StateChanged += (_, _) => Ui(Render);
        _engine.MessageReady += _ => Ui(Render);
        _engine.CoolingStarted += (_, _) => Ui(Render);
        _engine.CoolingCompleted += (_, _) => Ui(Render);
        _timer.Tick += (_, _) => Render();
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Development Plan", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Render(); }
    }

    private void Render()
    {
        if (IsDisposed) return;
        var state = _engine.State;
        _status.Text = state.Status.ToString();
        _phase.Text = state.Status == DevelopmentTaskEngineStatus.Cooling ? "Cooling — no delivery" : state.Status == DevelopmentTaskEngineStatus.Working ? "Working" : state.Status.ToString();
        _message.Text = $"Message: {state.CurrentMessageIndex + 1} / {Math.Max(0, state.TotalMessages)}";
        _recipients.Text = $"Last Chat: {state.LastMonitorId ?? "—"}    Tab: {state.LastTabId ?? "—"}";
        _delivery.Text = $"Last verified message: {(state.LastDeliveredMessageIndex >= 0 ? (state.LastDeliveredMessageIndex + 1).ToString() : "—")}    Revision: {state.Revision}";
        var now = DateTimeOffset.UtcNow;
        var remaining = state.Status == DevelopmentTaskEngineStatus.Working && state.WorkWindowStartedAt.HasValue
            ? TimeSpan.FromMinutes(10) - (now - state.WorkWindowStartedAt.Value)
            : state.Status == DevelopmentTaskEngineStatus.Cooling && state.CoolingStartedAt.HasValue
                ? TimeSpan.FromMinutes(5) - (now - state.CoolingStartedAt.Value)
                : TimeSpan.Zero;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        _countdown.Text = $"Time remaining: {remaining:mm\:ss}";
        _start.Enabled = state.Status is DevelopmentTaskEngineStatus.Stopped or DevelopmentTaskEngineStatus.Paused;
        _pause.Enabled = state.Status == DevelopmentTaskEngineStatus.Working;
        _resume.Enabled = state.Status is DevelopmentTaskEngineStatus.Paused or DevelopmentTaskEngineStatus.Stopped;
        _stop.Enabled = state.Status is not DevelopmentTaskEngineStatus.Stopped and not DevelopmentTaskEngineStatus.Completed;
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
