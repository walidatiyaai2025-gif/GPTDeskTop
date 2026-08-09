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
    private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new()
    {
        Text = "Ready",
        Dock = DockStyle.Fill,
        ForeColor = FluentTheme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        AccessibleRole = AccessibleRole.StatusBar
    };

    private bool _busy;

    public SettingsForm(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Text = "GPTDeskTop Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(720, 520);
        ClientSize = new Size(840, 640);
        _soundType.Items.AddRange(new object[] { "Asterisk", "Exclamation", "Beep", "Hand" });

        BuildUi();
        ConfigureAccessibility();
        WireEvents();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_saveButton, primary: true);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
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

        _tabs.TabPages.Add(BuildMonitoringTab());
        _tabs.TabPages.Add(BuildRotationTab());
        _tabs.TabPages.Add(BuildNotificationsTab());

        var statusHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FluentTheme.SurfaceAlt,
            Padding = new Padding(10, 2, 10, 2),
            Margin = new Padding(0, 6, 0, 0)
        };
        statusHost.Controls.Add(_statusLabel);

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
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(statusHost, 0, 2);
        root.Controls.Add(buttons, 0, 3);
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

    private void WireEvents()
    {
        Shown += async (_, _) =>
        {
            await LoadSettingsAsync();
            if (!_busy)
            {
                _defaultReply.Focus();
                _defaultReply.SelectAll();
            }
        };
        _saveButton.Click += async (_, _) => await SaveSettingsAsync();
        _soundEnabled.CheckedChanged += (_, _) => UpdateDependentControls();
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "GPTDeskTop application settings";
        AccessibleDescription = "Configure monitoring defaults, conversation rotation, recovery and desktop notifications.";

        ConfigureAccessible(_defaultReply, "Default auto reply", "Message sent after a completed assistant response.", 0);
        ConfigureAccessible(_defaultDelay, "Default reply delay", "Seconds to wait before sending the automatic reply.", 1);
        ConfigureAccessible(_defaultTimer, "Default polling timer", "Seconds between monitor checks.", 2);
        ConfigureAccessible(_noResponseRefresh, "No response refresh timeout", "Seconds without a new assistant response before refreshing the monitored tab.", 3);

        ConfigureAccessible(_rotateAfterMessages, "Assistant message rotation threshold", "Number of assistant messages before proactive conversation rotation. Zero disables it.", 0);
        ConfigureAccessible(_messageCountRotationStartMessage, "Rotation start message", "Message sent in the new conversation after message-count rotation.", 1);
        ConfigureAccessible(_timeoutRecovery, "Timeout recovery message", "Message sent to a recovery conversation after a delivery timeout.", 2);

        ConfigureAccessible(_notificationDuration, "Notification duration", "Desktop notification display duration in seconds.", 0);
        ConfigureAccessible(_soundType, "Notification sound type", "Windows sound used for desktop notifications.", 1);
        ConfigureAccessible(_soundEnabled, "Enable notification sound", "Play the selected Windows sound with desktop notifications.", 2);

        _tabs.AccessibleName = "Settings categories";
        _tabs.TabIndex = 0;
        _statusLabel.AccessibleName = "Settings operation status";
        _saveButton.AccessibleName = "Save application settings";
        _saveButton.TabIndex = 0;
        _cancelButton.AccessibleName = "Cancel settings changes";
        _cancelButton.TabIndex = 1;
    }

    private static void ConfigureAccessible(Control control, string name, string description, int tabIndex)
    {
        control.AccessibleName = name;
        control.AccessibleDescription = description;
        control.TabIndex = tabIndex;
    }

    private void UpdateDependentControls()
    {
        _soundType.Enabled = _soundEnabled.Checked && !_busy;
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        _tabs.Enabled = !busy;
        _saveButton.Enabled = !busy;
        UseWaitCursor = busy;
        _statusLabel.Text = status;
        _statusLabel.ForeColor = busy ? FluentTheme.Accent : FluentTheme.Muted;
        UpdateDependentControls();
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
        SetBusy(true, "Loading settings…");
        try
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
            SetBusy(false, "Settings loaded. Changes are not applied until you choose Save Settings.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Settings could not be loaded.");
            MessageBox.Show(this, $"GPTDeskTop could not load application settings.\n\n{ex.Message}", "Settings Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_busy) return;

        var rawRotationStartMessage = _messageCountRotationStartMessage.Text.Trim();
        if (_rotateAfterMessages.Value > 0 && string.IsNullOrWhiteSpace(rawRotationStartMessage))
        {
            _tabs.SelectedIndex = 1;
            _messageCountRotationStartMessage.Focus();
            MessageBox.Show(this, "New Chat start message cannot be empty when message-count rotation is enabled.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var rotationStartMessage = string.IsNullOrWhiteSpace(rawRotationStartMessage) ? "كمل" : rawRotationStartMessage;

        SetBusy(true, "Saving settings…");
        try
        {
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
            _statusLabel.Text = "Settings saved.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, "Settings were not saved. Review the error and try again.");
            MessageBox.Show(this, $"GPTDeskTop could not save application settings.\n\n{ex.Message}", "Settings Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
