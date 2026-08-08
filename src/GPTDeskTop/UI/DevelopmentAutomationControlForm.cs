using GPTDeskTop.Data;
using GPTDeskTop.Models;
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
    private readonly Label _nextMessage = new();
    private readonly Label _status = new();
    private readonly CheckedListBox _monitors = new();
    private readonly TextBox _planId = new();
    private readonly TextBox _planTitle = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };
    private readonly Dictionary<long, SavedMonitor> _monitorMap = new();
    private bool _loading;

    public DevelopmentAutomationControlForm(TaskAutomationService automation, LocalDatabase database)
    {
        _automation = automation;
        _database = database;
        Text = "Development Automation Controls";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 650);
        Size = new Size(1040, 720);
        BuildUi();
        FluentTheme.Apply(this);
        var runNowButton = Controls.Find("RunNowButton", true).OfType<Button>().FirstOrDefault();
        if (runNowButton is not null)
            FluentTheme.StyleButton(runNowButton, primary: true);
        WireEvents();
        _refreshTimer.Start();
        Shown += async (_, _) => await RefreshAllAsync();
        FormClosed += (_, _) => _refreshTimer.Stop();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 7,
            BackColor = FluentTheme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var monitorPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        monitorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        monitorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitorPanel.Controls.Add(new Label { Text = "Development Automation Opt-in", Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold), ForeColor = FluentTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _monitors.Dock = DockStyle.Fill;
        _monitors.CheckOnClick = true;
        monitorPanel.Controls.Add(_monitors, 0, 1);
        root.Controls.Add(monitorPanel, 0, 0);

        var planPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10, 0, 0, 0) };
        planPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        planPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planPanel.Controls.Add(new Label { Text = "Plan ID", Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        planPanel.Controls.Add(_planId, 1, 0);
        planPanel.Controls.Add(new Label { Text = "Plan Title", Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        planPanel.Controls.Add(_planTitle, 1, 1);
        var hint = new Label { Text = "Select a monitor, edit its plan metadata, then press Save Selection. Unchecked monitors are never targeted by the development-plan worker.", Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, AutoEllipsis = true };
        planPanel.Controls.Add(hint, 0, 2);
        planPanel.SetColumnSpan(hint, 2);
        root.Controls.Add(planPanel, 1, 0);

        AddRow(root, 1, "Phase", _phase);
        AddRow(root, 2, "Window", _timer);
        AddRow(root, 3, "Checkpoint", _checkpoint);
        AddRow(root, 4, "Current Message", _message);
        AddRow(root, 5, "Next Message", _nextMessage);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        var save = new Button { Name = "SaveSelectionButton", Text = "Save Selection", AutoSize = true };
        var runNow = new Button { Name = "RunNowButton", Text = "Run Now", AutoSize = true };
        var pause = new Button { Name = "PauseButton", Text = "Pause", AutoSize = true };
        var resume = new Button { Name = "ResumeButton", Text = "Resume", AutoSize = true };
        var stop = new Button { Name = "StopButton", Text = "Stop", AutoSize = true };
        var refresh = new Button { Name = "RefreshButton", Text = "Refresh", AutoSize = true };
        actions.Controls.AddRange([save, runNow, pause, resume, stop, refresh]);
        root.Controls.Add(actions, 0, 6);
        root.SetColumnSpan(actions, 2);

        var runtimePanel = new Panel { Dock = DockStyle.Fill };
        _status.Text = "—";
        _status.Dock = DockStyle.Fill;
        _status.Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold);
        _status.ForeColor = FluentTheme.Text;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        runtimePanel.Controls.Add(_status);
        root.Controls.Add(runtimePanel, 0, 5);

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
        value.Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold);
        value.ForeColor = FluentTheme.Text;
        value.AutoEllipsis = true;
        value.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(label, 0, row);
        root.Controls.Add(value, 1, row);
    }

    private void WireEvents()
    {
        var save = Controls.Find("SaveSelectionButton", true).OfType<Button>().First();
        var runNow = Controls.Find("RunNowButton", true).OfType<Button>().First();
        var pause = Controls.Find("PauseButton", true).OfType<Button>().First();
        var resume = Controls.Find("ResumeButton", true).OfType<Button>().First();
        var stop = Controls.Find("StopButton", true).OfType<Button>().First();
        var refresh = Controls.Find("RefreshButton", true).OfType<Button>().First();

        save.Click += async (_, _) => await SaveSelectionAsync();
        runNow.Click += async (_, _) => await RunNowAsync();
        pause.Click += async (_, _) => await PauseAsync();
        resume.Click += async (_, _) => await ResumeAsync();
        stop.Click += async (_, _) => await StopAsync();
        refresh.Click += async (_, _) => await RefreshAllAsync();
        _monitors.SelectedIndexChanged += async (_, _) => await LoadSelectedPlanAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshStateAsync();
    }

    private async Task LoadMonitorsAsync()
    {
        _loading = true;
        try
        {
            var monitors = await _database.GetSavedMonitorsAsync();
            _monitorMap.Clear();
            _monitors.Items.Clear();
            foreach (var monitor in monitors.Where(m => m.Enabled))
            {
                _monitorMap[monitor.Id] = monitor;
                var enabled = await _database.GetSettingAsync($"TaskAutomation.Monitor.{monitor.Id}.Enabled") == "1";
                _monitors.Items.Add(new MonitorItem(monitor.Id, monitor.Title, monitor.Url), enabled);
            }
        }
        finally { _loading = false; }
        await LoadSelectedPlanAsync();
    }

    private async Task LoadSelectedPlanAsync()
    {
        if (_loading || _monitors.SelectedItem is not MonitorItem item)
            return;
        _planId.Text = await _database.GetSettingAsync($"TaskAutomation.Monitor.{item.Id}.PlanId") ?? "default-development-plan";
        _planTitle.Text = await _database.GetSettingAsync($"TaskAutomation.Monitor.{item.Id}.PlanTitle") ?? "GPTDeskTop Development Plan";
    }

    private async Task SaveSelectionAsync()
    {
        try
        {
            foreach (var item in _monitorMap.Values)
            {
                var index = FindMonitorIndex(item.Id);
                var selected = index >= 0 && _monitors.GetItemChecked(index);
                await _database.SetSettingAsync($"TaskAutomation.Monitor.{item.Id}.Enabled", selected ? "1" : "0");
            }

            if (_monitors.SelectedItem is MonitorItem selectedItem)
            {
                await _database.SetSettingAsync($"TaskAutomation.Monitor.{selectedItem.Id}.PlanId", string.IsNullOrWhiteSpace(_planId.Text) ? "default-development-plan" : _planId.Text.Trim());
                await _database.SetSettingAsync($"TaskAutomation.Monitor.{selectedItem.Id}.PlanTitle", string.IsNullOrWhiteSpace(_planTitle.Text) ? "GPTDeskTop Development Plan" : _planTitle.Text.Trim());
            }

            _status.Text = "Development Automation selection saved.";
            await RefreshAllAsync();
        }
        catch (Exception ex) { _status.Text = $"Save failed: {ex.Message}"; }
    }

    private int FindMonitorIndex(long id)
    {
        for (var i = 0; i < _monitors.Items.Count; i++)
            if (_monitors.Items[i] is MonitorItem item && item.Id == id) return i;
        return -1;
    }

    private async Task RunNowAsync()
    {
        try
        {
            await SaveSelectionAsync();
            await _automation.StopAsync();
            await _database.SetSettingAsync("TaskAutomation.Phase", "Working");
            await _database.SetSettingAsync("TaskAutomation.WorkWindowStartedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await _database.SetSettingAsync("TaskAutomation.CoolingStartedUtc", string.Empty);
            await _automation.StartAsync();
            _status.Text = "Manual run started for opted-in monitors.";
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
            if (!Parse(await _database.GetSettingAsync("TaskAutomation.WorkWindowStartedUtc")).HasValue)
                await _database.SetSettingAsync("TaskAutomation.WorkWindowStartedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await _automation.StartAsync();
            _status.Text = "Automation resumed from persisted message checkpoints.";
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
            _status.Text = "Automation stopped. Checkpoints remain available for later resume.";
            await RefreshStateAsync();
        }
        catch (Exception ex) { _status.Text = $"Stop failed: {ex.Message}"; }
    }

    private async Task RefreshAllAsync()
    {
        await LoadMonitorsAsync();
        await RefreshStateAsync();
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
            _message.Text = await _database.GetSettingAsync("TaskAutomation.CurrentMessage") ?? "No current message.";
            _nextMessage.Text = await _database.GetSettingAsync("TaskAutomation.NextMessage") ?? "No next message.";
            if (string.IsNullOrWhiteSpace(_status.Text) || _status.Text.StartsWith("No cycle", StringComparison.Ordinal))
                _status.Text = _automation.IsRunning ? "Worker running" : "Worker stopped";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private static DateTimeOffset? Parse(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed record MonitorItem(long Id, string Title, string Url)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Title) ? Url : Title;
    }
}
