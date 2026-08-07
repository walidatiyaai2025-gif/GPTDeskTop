using System.Media;
using GPTDeskTop.Data;
using GPTDeskTop.UI;

namespace GPTDeskTop.Services;

public sealed class TrayNotificationService : IDisposable
{
    private readonly ChatGptMonitorService _monitor;
    private readonly LocalDatabase _database;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _durationMenu;
    private int _durationSeconds = 8;
    private bool _soundEnabled = true;
    private string _soundType = "Asterisk";
    private bool _disposed;

    public TrayNotificationService(ChatGptMonitorService monitor, LocalDatabase database)
    {
        _monitor = monitor; _database = database;
        _durationMenu = new ToolStripMenuItem("Balloon duration");
        foreach (var seconds in new[] { 3, 5, 8, 10, 15, 30 })
        {
            var item = new ToolStripMenuItem($"{seconds} seconds") { Tag = seconds };
            item.Click += async (_, _) => await SetDurationAsync(seconds);
            _durationMenu.DropDownItems.Add(item);
        }
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += async (_, _) => await OpenSettingsAsync();
        var menu = FluentTheme.CreateMenu();
        menu.Items.Add(new ToolStripMenuItem("GPTDeskTop notifications") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(settingsItem); menu.Items.Add(_durationMenu);
        _notifyIcon = new NotifyIcon { Icon = SystemIcons.Information, Text = "GPTDeskTop Chat Monitor", Visible = true, ContextMenuStrip = menu };
        _monitor.ResponseReceived += OnResponseReceived;
    }

    public async Task InitializeAsync() => await ReloadSettingsAsync();

    private async Task ReloadSettingsAsync()
    {
        _durationSeconds = await _database.GetIntSettingAsync("NotificationDurationSeconds", 8, 1, 60);
        _soundEnabled = !string.Equals(await _database.GetSettingAsync("NotificationSoundEnabled"), "0", StringComparison.Ordinal);
        _soundType = await _database.GetSettingAsync("NotificationSoundType") ?? "Asterisk";
        UpdateMenuChecks();
    }

    private void OnResponseReceived(long monitorId, string title, string response, bool isError)
    {
        if (_disposed) return;
        try
        {
            if (_soundEnabled) PlaySound(isError ? "Exclamation" : _soundType);
            var caption = isError ? $"GPTDeskTop - ERROR - Monitor #{monitorId}" : $"GPTDeskTop - Reply - Monitor #{monitorId}";
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "ChatGPT" : title.Trim();
            _notifyIcon.ShowBalloonTip(_durationSeconds * 1000, caption, $"{safeTitle}\n{Shorten(response, 220)}", isError ? ToolTipIcon.Error : ToolTipIcon.Info);
        }
        catch { }
    }

    private static void PlaySound(string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "exclamation": SystemSounds.Exclamation.Play(); break;
            case "beep": SystemSounds.Beep.Play(); break;
            case "hand": SystemSounds.Hand.Play(); break;
            default: SystemSounds.Asterisk.Play(); break;
        }
    }

    private async Task OpenSettingsAsync()
    {
        using var form = new SettingsForm(_database);
        if (form.ShowDialog() != DialogResult.OK) return;
        await ReloadSettingsAsync();
        if (_soundEnabled) PlaySound(_soundType);
        _notifyIcon.ShowBalloonTip(Math.Min(_durationSeconds, 5) * 1000, "GPTDeskTop", "Settings saved.", ToolTipIcon.Info);
    }

    private async Task SetDurationAsync(int seconds)
    {
        _durationSeconds = Math.Clamp(seconds, 1, 60);
        await _database.SetSettingAsync("NotificationDurationSeconds", _durationSeconds.ToString());
        UpdateMenuChecks();
    }

    private void UpdateMenuChecks()
    {
        foreach (ToolStripItem raw in _durationMenu.DropDownItems)
            if (raw is ToolStripMenuItem item && item.Tag is int seconds) item.Checked = seconds == _durationSeconds;
    }

    private static string Shorten(string text, int max)
    {
        var value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _monitor.ResponseReceived -= OnResponseReceived; _notifyIcon.Visible = false; _notifyIcon.Dispose();
    }
}
