using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed class HomeMetricsService : IDisposable
{
    private readonly LocalDatabase _database;
    private readonly ChatGptMonitorService _monitor;
    private readonly Label _crashCard;
    private readonly Label _monitorCard;
    private readonly List<DataGridView> _statusGrids = new();
    private bool _disposed;

    public HomeMetricsService(Form form, LocalDatabase database, ChatGptMonitorService monitor)
    {
        _database = database;
        _monitor = monitor;

        _crashCard = CreateCard("Crashes", "0");
        _monitorCard = CreateCard("Monitors", "0");

        var toolbar = FindControls<FlowLayoutPanel>(form).FirstOrDefault();
        if (toolbar is not null)
        {
            toolbar.Controls.Add(_monitorCard);
            toolbar.Controls.Add(_crashCard);
        }
        else
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(Math.Max(0, form.ClientSize.Width - 340), 10)
            };
            panel.Controls.Add(_monitorCard);
            panel.Controls.Add(_crashCard);
            form.Controls.Add(panel);
            panel.BringToFront();
        }

        foreach (var grid in FindControls<DataGridView>(form))
        {
            if (grid.Columns.Cast<DataGridViewColumn>().Any(c => string.Equals(c.HeaderText, "Status", StringComparison.OrdinalIgnoreCase)))
            {
                grid.CellFormatting += OnStatusCellFormatting;
                _statusGrids.Add(grid);
            }
        }

        form.Shown += OnShown;
        _monitor.RunningStateChanged += OnRunningStateChanged;
    }

    private static Label CreateCard(string title, string value)
        => new()
        {
            Text = $"{title}\r\n{value}",
            AutoSize = false,
            Width = 132,
            Height = 52,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(4),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(32, 31, 30),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold)
        };

    private void OnStatusCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!string.Equals(grid.Columns[e.ColumnIndex].HeaderText, "Status", StringComparison.OrdinalIgnoreCase)) return;

        var value = e.Value?.ToString() ?? string.Empty;
        if (value.Contains("Running", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = "● Running";
            e.CellStyle.ForeColor = Color.SeaGreen;
            e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
            e.FormattingApplied = true;
        }
        else if (value.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = "● Stopped";
            e.CellStyle.ForeColor = Color.Firebrick;
            e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
            e.FormattingApplied = true;
        }
    }

    private async void OnShown(object? sender, EventArgs e) => await RefreshAsync();
    private async void OnRunningStateChanged() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        try
        {
            var crashCount = await _database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue);
            var monitors = await _database.GetSavedMonitorsAsync();
            var running = monitors.Count(m => _monitor.IsMonitorRunning(m.Id));
            SetText(_crashCard, $"Crashes\r\n{crashCount}");
            SetText(_monitorCard, $"Monitors\r\n{running} / {monitors.Count}");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "HomeMetricsService.RefreshAsync");
        }
    }

    private static void SetText(Control control, string value)
    {
        if (control.IsDisposed) return;
        if (control.InvokeRequired) control.BeginInvoke(new Action(() => control.Text = value));
        else control.Text = value;
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var nested in FindControls<T>(child)) yield return nested;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.RunningStateChanged -= OnRunningStateChanged;
        foreach (var grid in _statusGrids) grid.CellFormatting -= OnStatusCellFormatting;
    }
}
