using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

public sealed class SettingsForm : Form
{
    private readonly LocalDatabase _database;
    private readonly NumericUpDown _defaultDelay = new() { Minimum = 0, Maximum = 300, Width = 120 };
    private readonly NumericUpDown _defaultTimer = new() { Minimum = 1, Maximum = 60, Width = 120 };
    private readonly NumericUpDown _rotateAfterMessages = new() { Minimum = 0, Maximum = 10000, Width = 120 };
    private readonly NumericUpDown _noResponseRefresh = new() { Minimum = 30, Maximum = 3600, Width = 120, Increment = 30 };
    private readonly NumericUpDown _notificationDuration = new() { Minimum = 1, Maximum = 60, Width = 120 };
    private readonly TextBox _defaultReply = new() { Width = 220, Text = "كمل" };
    private readonly TextBox _messageCountRotationStartMessage = new() { Width = 220, Text = "كمل" };
    private readonly TextBox _timeoutRecovery = new() { Width = 220, Text = "كمل" };
    private readonly CheckBox _soundEnabled = new() { Text = "Play sound with balloon notifications", AutoSize = true };
    private readonly ComboBox _soundType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _saveButton = new() { Text = "Save Settings", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public SettingsForm(LocalDatabase database)
    {
        _database = database;
        Text = "GPTDeskTop Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(660, 525);
        _soundType.Items.AddRange(new object[] { "Asterisk", "Exclamation", "Beep", "Hand" });
        BuildUi();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_saveButton, primary: true);
        Shown += async (_, _) => await LoadSettingsAsync();
        _saveButton.Click += async (_, _) => await SaveSettingsAsync();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 12 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        for (var i = 0; i < 11; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(root, 0, "Default auto reply for new monitors", _defaultReply);
        AddRow(root, 1, "Default reply delay (seconds)", _defaultDelay);
        AddRow(root, 2, "Default monitor timer (seconds)", _defaultTimer);
        AddRow(root, 3, "Rotate after assistant messages (0 = off)", _rotateAfterMessages);
        AddRow(root, 4, "Message-count new Chat start message", _messageCountRotationStartMessage);
        AddRow(root, 5, "No-response refresh timeout (seconds)", _noResponseRefresh);
        AddRow(root, 6, "Timeout recovery message", _timeoutRecovery);
        AddRow(root, 7, "Balloon duration (seconds)", _notificationDuration);
        root.Controls.Add(_soundEnabled, 0, 8);
        root.SetColumnSpan(_soundEnabled, 2);
        AddRow(root, 9, "Balloon sound", _soundType);

        var note = new Label
        {
            Text = "Message-count rotation uses the visible assistant-response count in the current ChatGPT conversation. When the configured count is reached, an enabled Conversation Rotation monitor opens a new chat, sends the fixed message above, keeps the same Monitor ID, and continues monitoring. Set the count to 0 to disable proactive rotation.",
            ForeColor = FluentTheme.Muted,
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        root.Controls.Add(note, 0, 10);
        root.SetColumnSpan(note, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);
        root.Controls.Add(buttons, 0, 11);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel root, int row, string text, Control control)
    {
        root.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Anchor = AnchorStyles.Left;
        root.Controls.Add(control, 1, row);
    }

    private async Task LoadSettingsAsync()
    {
        _defaultReply.Text = await _database.GetSettingAsync("DefaultAutoReply") ?? "كمل";
        _defaultDelay.Value = await _database.GetIntSettingAsync("DefaultMonitorDelaySeconds", 3, 0, 300);
        _defaultTimer.Value = await _database.GetIntSettingAsync("DefaultMonitorTimerSeconds", 1, 1, 60);
        _rotateAfterMessages.Value = await _database.GetIntSettingAsync("RotateAfterAssistantMessages", 0, 0, 10000);
        _messageCountRotationStartMessage.Text = await _database.GetSettingAsync("MessageCountRotationStartMessage") ?? "كمل";
        _noResponseRefresh.Value = await _database.GetIntSettingAsync("NoResponseRefreshSeconds", 180, 30, 3600);
        _timeoutRecovery.Text = await _database.GetSettingAsync("TimeoutRecoveryMessage") ?? "كمل";
        _notificationDuration.Value = await _database.GetIntSettingAsync("NotificationDurationSeconds", 8, 1, 60);
        _soundEnabled.Checked = !string.Equals(await _database.GetSettingAsync("NotificationSoundEnabled"), "0", StringComparison.Ordinal);
        var sound = await _database.GetSettingAsync("NotificationSoundType") ?? "Asterisk";
        _soundType.SelectedItem = _soundType.Items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), sound, StringComparison.OrdinalIgnoreCase)) ?? "Asterisk";
    }

    private async Task SaveSettingsAsync()
    {
        var rotationStartMessage = string.IsNullOrWhiteSpace(_messageCountRotationStartMessage.Text) ? "كمل" : _messageCountRotationStartMessage.Text.Trim();
        if (_rotateAfterMessages.Value > 0 && string.IsNullOrWhiteSpace(rotationStartMessage))
        {
            MessageBox.Show(this, "New Chat start message cannot be empty when message-count rotation is enabled.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _database.SetSettingAsync("DefaultAutoReply", string.IsNullOrWhiteSpace(_defaultReply.Text) ? "كمل" : _defaultReply.Text.Trim());
        await _database.SetSettingAsync("DefaultMonitorDelaySeconds", ((int)_defaultDelay.Value).ToString());
        await _database.SetSettingAsync("DefaultMonitorTimerSeconds", ((int)_defaultTimer.Value).ToString());
        await _database.SetSettingAsync("RotateAfterAssistantMessages", ((int)_rotateAfterMessages.Value).ToString());
        await _database.SetSettingAsync("MessageCountRotationStartMessage", rotationStartMessage);
        await _database.SetSettingAsync("NoResponseRefreshSeconds", ((int)_noResponseRefresh.Value).ToString());
        await _database.SetSettingAsync("TimeoutRecoveryMessage", string.IsNullOrWhiteSpace(_timeoutRecovery.Text) ? "كمل" : _timeoutRecovery.Text.Trim());
        await _database.SetSettingAsync("NotificationDurationSeconds", ((int)_notificationDuration.Value).ToString());
        await _database.SetSettingAsync("NotificationSoundEnabled", _soundEnabled.Checked ? "1" : "0");
        await _database.SetSettingAsync("NotificationSoundType", _soundType.SelectedItem?.ToString() ?? "Asterisk");
        DialogResult = DialogResult.OK;
        Close();
    }
}
