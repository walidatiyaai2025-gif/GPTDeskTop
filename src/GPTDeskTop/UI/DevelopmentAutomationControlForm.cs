using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class DevelopmentAutomationControlForm : Form
{
    private readonly TaskAutomationService _automation;
    private readonly LocalDatabase _database;
    private readonly Label _phase = new();
    private readonly Label _timer = new();
    private readonly Label _checkpoint = new();
    private readonly Label _message = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };

    public DevelopmentAutomationControlForm(TaskAutomationService automation, LocalDatabase database)
    {
        _automation = automation;
        _database = database;
        Text = "Development Automation Controls";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 420);
        Size = new Size(820, 520);
        BuildUi();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(Controls.Find("RunNowButton", true).OfType<Button>().First(), primary: true);
        WireEvents();
        _refreshTimer.Start();
        Shown += async (_, _) => await RefreshStateAsync();
        FormClosed += (_, _) => _refreshTimer.Stop();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 6,
            BackColor = FluentTheme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(root, 0, "Phase", _phase);
        AddRow(root, 1, "Window", _timer);
        AddRow(root, 2, "Checkpoint", _checkpoint);
        AddRow(root, 3, "Current Message", _message);
        AddRow(root, 4, "Runtime", _status);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        var runNow = new Button { Name = "RunNowButton", Text = "Run Now", AutoSize = true };
        var pause = new Button { Name = "PauseButton", Text = "Pause", AutoSize = true };
        var resume = new Button { Name = "ResumeButton", Text = "Resume", AutoSize = true };
        var stop = new Button { Name = "StopButton", Text = "Stop", AutoSize = true };
        var refresh = new Button { Name = "RefreshButton", Text = "Refresh", AutoSize = true };
        actions.Controls.AddRange([runNow, pause, resume, stop, refresh]);
        root.Controls.Add(actions, 1, 5);
        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel root, int row, string caption, Label value)
    {
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold),
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.Font = new Font("Segoe UI Variable Text", 11F, FontStyle.Bold);
        value.ForeColor = FluentTheme.Text;
        value.AutoEllipsis = true;
        value.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(label, 0, row);
        root.Controls.Add(value, 1, row);
    }

    private void WireEvents()
    {
        var runNow = Controls.Find("RunNowButton", true).OfType<Button>().First();
        var pause = Controls.Find("PauseButton", true).OfType<Button>().First();
        var resume = Controls.Find("ResumeButton", true).OfType<Button>().First();
        var stop = Controls.Find("StopButton", true).OfType<Button>().First();
        var refresh = Controls.Find("RefreshButton", true).OfType<Button>().First();

        runNow.Click += async (_, _) => await RunNowAsync();
        pause.Click += async (_, _) => await PauseAsync();
        resume.Click += async (_, _) => await ResumeAsync();
        stop.Click += async (_, _) => await StopAsync();
        refresh.Click += async (_, _) => await RefreshStateAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshStateAsync();
    }

    private async Task RunNowAsync()
    {
        try
        {
            await _automation.StopAsync();
            await _database.SetSettingAsync("TaskAutomation.Phase", "Working");
            await _database.SetSettingAsync("TaskAutomation.WorkWindowStartedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await _database.SetSettingAsync("TaskAutomation.CoolingStartedUtc", string.Empty);
            await _automation.StartAsync();
            _status.Text = "Manual run started from a fresh work-window checkpoint.";
            await RefreshStateAsync();
        }
        catch (Exception ex) { _status.Text = $"Run Now failed: {ex.Message}"; }
    }

    private async Task PauseAsync()
    {
        try
        {
            await _automation.StopAsync();
            await _database.SetSettingAsync("TaskAutomation.Phase", "Paused");
            _status.Text = "Automation paused. Message checkpoints were preserved.";
            await RefreshStateAsync();
        }
        catch (Exception ex) { _status.Text = $"Pause failed: {ex.Message}"; }
    }

    private async Task ResumeAsync()
    {
        try
        {
            await _database.SetSettingAsync("TaskAutomation.Phase", "Working");
            await _database.SetSettingAsync("TaskAutomation.WorkWindowStartedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await _automation.StartAsync();
            _status.Text = "Automation resumed from the persisted message checkpoints.";
            await RefreshStateAsync();
        }
        catch (Exception ex) { _status.Text = $"Resume failed: {ex.Message}"; }
    }

    private async Task StopAsync()
    {
        try
        {
            await _automation.StopAsync();
            await _database.SetSettingAsync("TaskAutomation.Phase", "Stopped");
            _status.Text = "Automation stopped. Checkpoints remain available for a later resume.";
            await RefreshStateAsync();
        }
        catch (Exception ex) { _status.Text = $"Stop failed: {ex.Message}"; }
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            var phase = await _database.GetSettingAsync("TaskAutomation.Phase") ?? (_automation.IsRunning ? "Working" : "Idle");
            _phase.Text = phase;
            _phase.ForeColor = phase.Equals("Cooling", StringComparison.OrdinalIgnoreCase) ? FluentTheme.Muted : phase.Equals("Faulted", StringComparison.OrdinalIgnoreCase) ? Color.Firebrick : FluentTheme.Text;

            var workStart = Parse(await _database.GetSettingAsync("TaskAutomation.WorkWindowStartedUtc"));
            var coolingStart = Parse(await _database.GetSettingAsync("TaskAutomation.CoolingStartedUtc"));
            var workMinutes = await _database.GetIntSettingAsync("TaskAutomation.WorkWindowMinutes", 10, 1, 120);
            var coolingMinutes = await _database.GetIntSettingAsync("TaskAutomation.CoolingWindowMinutes", 5, 0, 120);
            var start = phase.Equals("Cooling", StringComparison.OrdinalIgnoreCase) ? coolingStart : workStart;
            var duration = phase.Equals("Cooling", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromMinutes(coolingMinutes) : TimeSpan.FromMinutes(workMinutes);
            if (start.HasValue && duration > TimeSpan.Zero)
            {
                var remaining = duration - (DateTimeOffset.UtcNow - start.Value);
                _timer.Text = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";
            }
            else _timer.Text = "—";

            var last = await _database.GetSettingAsync("TaskAutomation.LastCycleCompletedUtc");
            _checkpoint.Text = last is null ? "No cycle checkpoint yet" : last;
            var index = await _database.GetSettingAsync("TaskAutomation.LastCycleSentCount");
            _message.Text = index is null ? "No message delivered yet" : $"Last cycle sent: {index}";
            if (string.IsNullOrWhiteSpace(_status.Text) || _status.Text.StartsWith("No cycle", StringComparison.Ordinal))
                _status.Text = _automation.IsRunning ? "Worker running" : "Worker stopped";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private static DateTimeOffset? Parse(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
