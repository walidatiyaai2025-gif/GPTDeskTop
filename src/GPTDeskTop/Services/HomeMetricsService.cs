using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed class HomeMetricsService : IDisposable
{
    private readonly LocalDatabase _database;
    private readonly ChatGptMonitorService _monitor;
    private readonly Label _crashCard;
    private readonly Label _monitorCard;
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
        if (control.InvokeRequired) control.BeginInvoke(() => control.Text = value);
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
    }
}
