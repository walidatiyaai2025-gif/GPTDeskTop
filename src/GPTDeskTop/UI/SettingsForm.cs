using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

public sealed class SettingsForm : Form
{
    private readonly LocalDatabase _database;
    private readonly NumericUpDown _defaultDelay = new() { Minimum = 0, Maximum = 300, Width = 140 };
    private readonly NumericUpDown _defaultTimer = new() { Minimum = 1, Maximum = 60, Width = 140 };
    private readonly NumericUpDown _rotateAfterMessages = new() { Minimum = 0, Maximum = 10000, Width = 140 };
    private readonly NumericUpDown _noResponseRefresh = new() { Minimum = 30, Maximum = 3600, Width = 140, Increment = 30 };
    private readonly NumericUpDown _notificationDuration = new() { Minimum = 1, Maximum = 60, Width = 140 };
    private readonly TextBox _defaultReply = new() { Dock = DockStyle.Fill, Text = "كمل" };
    private readonly TextBox _messageCountRotationStartMessage = new() { Dock = DockStyle.Fill, Text = "كمل" };
    private readonly TextBox _timeoutRecovery = new() { Dock = DockStyle.Fill, Text = "كمل" };
    private readonly CheckBox _soundEnabled = new() { Text = "Play sound with balloon notifications", AutoSize = true };
    private readonly ComboBox _soundType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
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
        ClientSize = new Size(760, 560);
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Background };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        header.Controls.Add(new Label
        {
            Text = "Application Settings",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(FluentTheme.CreateMutedLabel("Configure monitoring defaults, conversation rotation/recovery and operator notifications."), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildMonitoringTab());
        tabs.TabPages.Add(BuildRotationTab());
        tabs.TabPages.Add(BuildNotificationsTab());

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = FluentTheme.Background,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private TabPage BuildMonitoringTab()
    {
        var page = CreateTab("Monitoring");
        var layout = CreateSettingsLayout(8);
        AddSectionTitle(layout, 0, "Monitor defaults", "Applied when a new monitor is created. Existing monitor-specific values are not overwritten.");
        AddRow(layout, 2, "Default auto reply", _defaultReply, "Message sent after a stable assistant response.");
        AddRow(layout, 3, "Reply delay", _defaultDelay, "Seconds to wait before sending the configured auto reply.");
        AddRow(layout, 4, "Polling timer", _defaultTimer, "Seconds between ChatGPT state checks for a running monitor.");
        AddRow(layout, 5, "No-response refresh", _noResponseRefresh, "If no new assistant response appears in this many seconds, only that tab is refreshed.");
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildRotationTab()
    {
        var page = CreateTab("Rotation & Recovery");
        var layout = CreateSettingsLayout(9);
        AddSectionTitle(layout, 0, "Conversation continuity", "Proactively rotate chats before they become too long while preserving the same Monitor ID.");
        AddRow(layout, 2, "Rotate after assistant messages (0 = off)", _rotateAfterMessages, "0 disables proactive message-count rotation. The current visible assistant count is used.");
        AddRow(layout, 3, "Message-count new Chat start message", _messageCountRotationStartMessage, "Fixed message sent after a successful message-count rotation.");
        AddSectionTitle(layout, 5, "Timeout recovery", "Used when ChatGPT reports a message-delivery timeout and a recovery chat is created.");
        AddRow(layout, 7, "Recovery message", _timeoutRecovery, "Message sent to the newly-created recovery conversation.");
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildNotificationsTab()
    {
        var page = CreateTab("Notifications");
        var layout = CreateSettingsLayout(8);
        AddSectionTitle(layout, 0, "Desktop notifications", "Control how long notifications remain visible and whether an operator sound is played.");
        AddRow(layout, 2, "Balloon duration", _notificationDuration, "Display duration in seconds.");
        AddRow(layout, 3, "Balloon sound", _soundType, "Windows notification sound used when sound is enabled.");
        layout.Controls.Add(_soundEnabled, 0, 5);
        layout.SetColumnSpan(_soundEnabled, 2);
        page.Controls.Add(layout);
        return page;
    }

    private static TabPage CreateTab(string title)
        => new() { Text = title, BackColor = FluentTheme.Surface, Padding = new Padding(18) };

    private static TableLayoutPanel CreateSettingsLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows,
            BackColor = FluentTheme.Surface,
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 0 or 5 ? 34 : 48));
        return layout;
    }

    private static void AddSectionTitle(TableLayoutPanel root, int row, string title, string subtitle)
    {
        var heading = FluentTheme.CreateSectionTitle(title);
        root.Controls.Add(heading, 0, row);
        root.SetColumnSpan(heading, 2);
        var description = FluentTheme.CreateMutedLabel(subtitle);
        root.Controls.Add(description, 0, row + 1);
        root.SetColumnSpan(description, 2);
    }

    private static void AddRow(TableLayoutPanel root, int row, string text, Control control, string hint)
    {
        var labelBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        labelBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        labelBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        labelBlock.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0);
        labelBlock.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), AutoEllipsis = true }, 0, 1);
        root.Controls.Add(labelBlock, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(8, 7, 6, 7);
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
