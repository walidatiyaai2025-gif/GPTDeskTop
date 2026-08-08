using System.Diagnostics;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class DevelopmentAutomationForm : Form
{
    private readonly TaskAutomationService _automation;
    private readonly LocalDatabase _database;
    private readonly Label _phase = new();
    private readonly Label _timer = new();
    private readonly Label _last = new();
    private readonly Label _error = new();
    private readonly TextBox _catalog = new() { Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button _start = new() { Text = "Start Now", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _editCatalog = new() { Text = "Edit task-messages.json", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timerTick = new() { Interval = 1000 };

    public DevelopmentAutomationForm(TaskAutomationService automation, LocalDatabase database)
    {
        _automation = automation;
        _database = database;
        Text = "Development Automation";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 560);
        Size = new Size(900, 680);
        BuildUi();
        WireEvents();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_start, primary: true);
        _timerTick.Start();
        Shown += async (_, _) => await RefreshAsync();
        FormClosed += (_, _) => _timerTick.Stop();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5, BackColor = FluentTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = FluentTheme.Surface, Padding = new Padding(16) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var title = new Label { Text = "Development Task Automation", Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold), ForeColor = FluentTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
        _phase.Text = "Idle"; _phase.Dock = DockStyle.Fill; _phase.TextAlign = ContentAlignment.MiddleRight; _phase.Font = new Font("Segoe UI Variable Text", 15F, FontStyle.Bold); _phase.ForeColor = FluentTheme.Muted;
        header.Controls.Add(title, 0, 0); header.Controls.Add(_phase, 1, 0);

        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 12, 0, 8) };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F)); stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F)); stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        _timer.Text = "--:--"; _last.Text = "Last cycle: —";
        foreach (var label in new[] { _timer, _last }) { label.Dock = DockStyle.Fill; label.Font = new Font("Segoe UI Variable Text", 11F, FontStyle.Bold); label.ForeColor = FluentTheme.Text; }
        stats.Controls.Add(MakeCard("Window timer", _timer), 0, 0); stats.Controls.Add(MakeCard("Last activity", _last), 1, 0); stats.Controls.Add(MakeCard("Engine", new Label { Text = "Checkpointed", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = FluentTheme.Text }), 2, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
        actions.Controls.Add(_start); actions.Controls.Add(_stop); actions.Controls.Add(_refresh); actions.Controls.Add(_editCatalog);
        _error.Text = ""; _error.Dock = DockStyle.Fill; _error.ForeColor = FluentTheme.Muted; _error.AutoEllipsis = true;

        var catalogGroup = new GroupBox { Text = "Task Message Catalog", Dock = DockStyle.Fill, Padding = new Padding(10), ForeColor = FluentTheme.Text, BackColor = FluentTheme.Background };
        catalogGroup.Controls.Add(_catalog);

        root.Controls.Add(header, 0, 0); root.Controls.Add(stats, 0, 1); root.Controls.Add(actions, 0, 2); root.Controls.Add(catalogGroup, 0, 3); root.Controls.Add(_error, 0, 4);
        Controls.Add(root);
    }

    private static Control MakeCard(string caption, Control value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = FluentTheme.Surface };
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 22, ForeColor = FluentTheme.Muted };
        value.Dock = DockStyle.Fill; panel.Controls.Add(value); panel.Controls.Add(label); return panel;
    }

    private void WireEvents()
    {
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _editCatalog.Click += (_, _) => EditCatalog();
        _timerTick.Tick += async (_, _) => await RefreshAsync(false);
        _automation.Activity += message => Ui(() => { _error.Text = message; _last.Text = $"Last activity: {DateTime.Now:HH:mm:ss}"; });
    }

    private async Task StartAsync()
    {
        try { await _automation.StartAsync(); await RefreshAsync(); }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private async Task StopAsync()
    {
        try { await _automation.StopAsync(); await RefreshAsync(); }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private void EditCatalog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "task-messages.json");
            if (!File.Exists(path)) File.WriteAllText(path, "[\n  \"كمل\"\n]\n");
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            _error.Text = "Opened task-messages.json in Notepad. Refresh after saving.";
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private async Task RefreshAsync(bool loadCatalog = true)
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
            DateTimeOffset? start = phase.Equals("Cooling", StringComparison.OrdinalIgnoreCase) ? coolingStart : workStart;
            var duration = phase.Equals("Cooling", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromMinutes(coolingMinutes) : TimeSpan.FromMinutes(workMinutes);
            if (start.HasValue && duration > TimeSpan.Zero)
            {
                var remaining = duration - (DateTimeOffset.UtcNow - start.Value);
                _timer.Text = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";
            }
            else _timer.Text = "--:--";

            var last = await _database.GetSettingAsync("TaskAutomation.LastCycleCompletedUtc");
            _last.Text = last is null ? "Last cycle: —" : $"Last cycle: {last}";
            var lastError = await _database.GetSettingAsync("TaskAutomation.LastError");
            if (!string.IsNullOrWhiteSpace(lastError)) _error.Text = lastError;
            if (loadCatalog)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "task-messages.json");
                _catalog.Text = File.Exists(path) ? await File.ReadAllTextAsync(path) : "task-messages.json not found.";
            }
        }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private static DateTimeOffset? Parse(string? raw) => DateTimeOffset.TryParse(raw, out var value) ? value : null;

    private void Ui(Action action) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(action); else action(); }
}
