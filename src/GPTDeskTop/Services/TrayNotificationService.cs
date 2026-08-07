using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public sealed class TrayNotificationService : IDisposable
{
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _durationMenu;
    private int _durationSeconds = 8;
    private bool _disposed;

    public TrayNotificationService(ChatGptMonitorService monitor, LocalDatabase database)
    {
        _monitor = monitor;
        _database = database;

        _durationMenu = new ToolStripMenuItem("Balloon duration");
        foreach (var seconds in new[] { 3, 5, 8, 10, 15, 30 })
        {
            var item = new ToolStripMenuItem($"{seconds} seconds") { Tag = seconds };
            item.Click += async (_, _) => await SetDurationAsync(seconds);
            _durationMenu.DropDownItems.Add(item);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("GPTDeskTop notifications") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_durationMenu);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "GPTDeskTop Chat Monitor",
            Visible = true,
            ContextMenuStrip = menu
        };

        _monitor.ResponseReceived += OnResponseReceived;
    }

    public async Task InitializeAsync()
    {
        var saved = await _database.GetSettingAsync("NotificationDurationSeconds");
        if (int.TryParse(saved, out var seconds))
            _durationSeconds = Math.Clamp(seconds, 1, 60);
        UpdateMenuChecks();
    }

    private void OnResponseReceived(long monitorId, string title, string response, bool isError)
    {
        if (_disposed)
            return;

        try
        {
            var caption = isError
                ? $"GPTDeskTop - ERROR - Monitor #{monitorId}"
                : $"GPTDeskTop - Reply - Monitor #{monitorId}";

            var safeTitle = string.IsNullOrWhiteSpace(title) ? "ChatGPT" : title.Trim();
            var body = $"{safeTitle}\n{Shorten(response, 220)}";
            var icon = isError ? ToolTipIcon.Error : ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(_durationSeconds * 1000, caption, body, icon);
        }
        catch
        {
            // Notifications must never interrupt monitor workers.
        }
    }

    private async Task SetDurationAsync(int seconds)
    {
        _durationSeconds = Math.Clamp(seconds, 1, 60);
        await _database.SetSettingAsync("NotificationDurationSeconds", _durationSeconds.ToString());
        UpdateMenuChecks();
        _notifyIcon.ShowBalloonTip(
            Math.Min(_durationSeconds, 5) * 1000,
            "GPTDeskTop",
            $"Notification duration saved: {_durationSeconds} seconds.",
            ToolTipIcon.Info);
    }

    private void UpdateMenuChecks()
    {
        foreach (ToolStripItem raw in _durationMenu.DropDownItems)
        {
            if (raw is ToolStripMenuItem item && item.Tag is int seconds)
                item.Checked = seconds == _durationSeconds;
        }
    }

    private static string Shorten(string text, int max)
    {
        var value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _monitor.ResponseReceived -= OnResponseReceived;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
