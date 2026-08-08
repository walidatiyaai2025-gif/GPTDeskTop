using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class DevelopmentAutomationForm : Form
{
    private readonly TaskAutomationService _automation;
    private readonly LocalDatabase _database;
    private readonly Label _phase = new() { AutoSize = true, Font = new Font("Segoe UI Variable Text", 18F, FontStyle.Bold) };
    private readonly Label _window = new() { AutoSize = true };
    private readonly Label _cycle = new() { AutoSize = true };
    private readonly Label _error = new() { AutoSize = true, MaximumSize = new Size(760, 0) };
    private readonly RichTextBox _activity = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public DevelopmentAutomationForm(TaskAutomationService automation, LocalDatabase database)
    {
        _automation = automation;
        _database = database;
        Text = "Development Automation";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 620);
        Size = new Size(980, 720);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([_start, _stop, _refresh]);
        root.Controls.Add(_phase, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_window, 0, 2);
        root.Controls.Add(_cycle, 0, 3);
        root.Controls.Add(_activity, 0, 4);
        Controls.Add(root);

        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _automation.Activity += OnActivity;
        FormClosed += (_, _) => { _timer.Stop(); _automation.Activity -= OnActivity; };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { _timer.Start(); await RefreshAsync(); };
    }

    private async Task StartAsync()
    {
        try { await _automation.StartAsync(); await RefreshAsync(); }
        catch (Exception ex) { Append($"Start failed: {ex.Message}"); }
    }

    private async Task StopAsync()
    {
        try { await _automation.StopAsync(); await RefreshAsync(); }
        catch (Exception ex) { Append($"Stop failed: {ex.Message}"); }
    }

    private async Task RefreshAsync()
    {
        if (IsDisposed) return;
        try
        {
            var monitors = await _database.GetSavedMonitorsAsync();
            var snapshot = await TaskAutomationDiagnosticsReader.ReadAsync(_database, monitors.Select(m => m.Id).ToArray());
            _phase.Text = $"Phase: {snapshot.Phase}{(_automation.IsRunning ? "  • RUNNING" : "  • STOPPED")}";
            _window.Text = $"Work: {Format(snapshot.WorkWindowStartedUtc)}    Cooling: {Format(snapshot.CoolingStartedUtc)}";
            _cycle.Text = $"Last cycle: {Format(snapshot.LastCycleCompletedUtc)}    Sent: {snapshot.LastCycleSentCount}";
            _error.Text = string.IsNullOrWhiteSpace(snapshot.LastError) ? "No automation error recorded." : $"Last error: {snapshot.LastError}";
            _error.ForeColor = string.IsNullOrWhiteSpace(snapshot.LastError) ? SystemColors.ControlText : Color.Firebrick;

            var lines = snapshot.Checkpoints.Select(pair =>
                $"Monitor {pair.Key}: {pair.Value.Status} | message {pair.Value.MessageIndex + 1} | next {pair.Value.NextMessageIndex + 1} | {Format(pair.Value.Timestamp)}");
            _activity.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) { Append($"Refresh failed: {ex.Message}"); }
    }

    private void OnActivity(string message) => Ui(() => Append(message));

    private void Append(string message)
    {
        if (_activity.IsDisposed) return;
        _activity.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private static string Format(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-";
}
