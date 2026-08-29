using GPTDeskTop.Models;

namespace GPTDeskTop.UI;

public sealed class MonitorSettingsForm : Form
{
    private readonly TextBox _autoReplyBox = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AutoSize = false };
    private readonly NumericUpDown _delaySeconds = new() { Minimum = 0, Maximum = 300, Width = 130 };
    private readonly NumericUpDown _timerSeconds = new() { Minimum = 1, Maximum = 60, Width = 130 };
    private readonly CheckBox _enabledCheck = new() { Text = "Monitor is enabled", AutoSize = true };
    private readonly CheckBox _rotationEnabledCheck = new() { Text = "Enable conversation rotation for this monitor", AutoSize = true };
    private readonly TextBox _newChatMessageBox = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AutoSize = false };
    private readonly NumericUpDown _newChatDelaySeconds = new() { Minimum = 0, Maximum = 600, Width = 130 };
    private readonly NumericUpDown _rotationCooldownSeconds = new() { Minimum = 0, Maximum = 3600, Width = 130 };
    private readonly NumericUpDown _maxRotations = new() { Minimum = 0, Maximum = 1000, Width = 130 };
    private readonly CheckBox _modelRoutingEnabledCheck = new() { Text = "Use model routing for new and recovery chats", AutoSize = true };
    private readonly TextBox _preferredModelBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Auto (leave current model)" };
    private readonly TextBox _fallbackModelBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Auto (leave current model)" };
    private readonly Button _saveButton = new() { Text = "Save Monitor", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _runtimeStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
        AutoEllipsis = true,
        Padding = new Padding(8, 2, 8, 2)
    };

    public string AutoReply => _autoReplyBox.Text.Trim();
    public int ReplyDelaySeconds => (int)_delaySeconds.Value;
    public int TimerSeconds => (int)_timerSeconds.Value;
    public bool MonitorEnabled => _enabledCheck.Checked;
    public bool ConversationRotationEnabled => _rotationEnabledCheck.Checked;
    public string NewChatStartMessage => _newChatMessageBox.Text.Trim();
    public int NewChatDelaySeconds => (int)_newChatDelaySeconds.Value;
    public int RotationCooldownSeconds => (int)_rotationCooldownSeconds.Value;
    public int MaxConversationRotations => (int)_maxRotations.Value;
    public bool ModelRoutingEnabled => _modelRoutingEnabledCheck.Checked;
    public string PreferredModel => string.IsNullOrWhiteSpace(_preferredModelBox.Text) ? "Auto" : _preferredModelBox.Text.Trim();
    public string FallbackModel => string.IsNullOrWhiteSpace(_fallbackModelBox.Text) ? PreferredModel : _fallbackModelBox.Text.Trim();

    public MonitorSettingsForm(string title, string url, SavedMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Text = "Monitor Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(740, 540);
        ClientSize = new Size(900, 680);

        _autoReplyBox.Text = string.IsNullOrWhiteSpace(monitor.AutoReply) ? "كمل" : monitor.AutoReply;
        _delaySeconds.Value = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        _timerSeconds.Value = Math.Clamp(monitor.TimerSeconds, 1, 60);
        _enabledCheck.Checked = monitor.Enabled;
        _rotationEnabledCheck.Checked = monitor.ConversationRotationEnabled;
        _newChatMessageBox.Text = string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage;
        _newChatDelaySeconds.Value = Math.Clamp(monitor.NewChatDelaySeconds, 0, 600);
        _rotationCooldownSeconds.Value = Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600);
        _maxRotations.Value = Math.Clamp(monitor.MaxConversationRotations, 0, 1000);
        _modelRoutingEnabledCheck.Checked = monitor.ModelRoutingEnabled;
        _preferredModelBox.Text = string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel;
        _fallbackModelBox.Text = string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel;

        BuildUi(title, url);
        ConfigureAccessibility();
        WireEvents();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_saveButton, primary: true);
        ApplyMonitorStatus(monitor);
        UpdateDependentControls();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void BuildUi(string title, string url)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = FluentTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = FluentTheme.Background,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        header.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(title) ? "ChatGPT Monitor" : title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Bold),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var statusHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 4, 0, 4),
            BackColor = FluentTheme.Background,
            Margin = Padding.Empty
        };
        statusHost.Controls.Add(_runtimeStatus);
        header.Controls.Add(statusHost, 1, 0);

        var description = FluentTheme.CreateMutedLabel("Configure this monitor without changing global defaults for other conversations.");
        header.Controls.Add(description, 0, 1);
        header.SetColumnSpan(description, 2);

        var urlLabel = new Label
        {
            Text = url,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = FluentTheme.Muted,
            Font = new Font("Segoe UI Variable Text", 8F),
            TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = "Monitored conversation URL",
            AccessibleDescription = url
        };
        header.Controls.Add(urlLabel, 0, 2);
        header.SetColumnSpan(urlLabel, 2);

        _tabs.TabPages.Add(BuildGeneralTab());
        _tabs.TabPages.Add(BuildRotationTab());
        _tabs.TabPages.Add(BuildModelTab());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = FluentTheme.Background,
            Padding = new Padding(0, 9, 0, 0)
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private TabPage BuildGeneralTab()
    {
        var page = CreateTab("General");
        var layout = CreateSettingsLayout(7);
        AddSectionTitle(layout, 0, "Response automation", "Core behavior for detecting a completed response and deciding when to continue the conversation.");
        layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 96);
        AddRow(layout, 2, "Auto reply", _autoReplyBox, "Message sent after a stable assistant response.");
        AddRow(layout, 3, "Delay before send", _delaySeconds, "Seconds to wait before sending the auto reply.");
        AddRow(layout, 4, "Monitor timer", _timerSeconds, "Polling interval in seconds.");
        layout.Controls.Add(_enabledCheck, 0, 5);
        layout.SetColumnSpan(_enabledCheck, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildRotationTab()
    {
        var page = CreateTab("Rotation");
        var layout = CreateSettingsLayout(9);
        AddSectionTitle(layout, 0, "Conversation rotation", "Applies to context-limit rotation and the global assistant-message-count threshold.");
        layout.Controls.Add(_rotationEnabledCheck, 0, 2);
        layout.SetColumnSpan(_rotationEnabledCheck, 2);
        layout.RowStyles[3] = new RowStyle(SizeType.Absolute, 96);
        AddRow(layout, 3, "Context-limit start message", _newChatMessageBox, "Fallback/handoff message used when ChatGPT reports the current conversation is too long.");
        AddRow(layout, 4, "New Chat delay", _newChatDelaySeconds, "Seconds to wait before opening and preparing the replacement conversation.");
        AddRow(layout, 5, "Rotation cooldown", _rotationCooldownSeconds, "Pause after a successful handoff before normal monitoring resumes.");
        AddRow(layout, 6, "Maximum rotations", _maxRotations, "0 allows unlimited successful rotations for this monitor.");
        var note = FluentTheme.CreateMutedLabel("The proactive message-count threshold and its fixed new-chat message are configured globally under Settings → Rotation & Recovery.");
        layout.Controls.Add(note, 0, 7);
        layout.SetColumnSpan(note, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildModelTab()
    {
        var page = CreateTab("Model Routing");
        var layout = CreateSettingsLayout(8);
        AddSectionTitle(layout, 0, "Conservative model routing", "Optional routing is applied only to newly-created rotation/recovery chats and never bypasses product usage limits.");
        layout.Controls.Add(_modelRoutingEnabledCheck, 0, 2);
        layout.SetColumnSpan(_modelRoutingEnabledCheck, 2);
        AddRow(layout, 3, "Preferred model label", _preferredModelBox, "Matched against the visible ChatGPT model picker. Use Auto to keep the current model.");
        AddRow(layout, 4, "Fallback model label", _fallbackModelBox, "Tried once only when the preferred model is not selectable.");
        page.Controls.Add(layout);
        return page;
    }

    private void WireEvents()
    {
        _saveButton.Click += (_, _) => TrySaveAndClose();
        _rotationEnabledCheck.CheckedChanged += (_, _) => UpdateDependentControls();
        _modelRoutingEnabledCheck.CheckedChanged += (_, _) => UpdateDependentControls();
        Shown += (_, _) =>
        {
            _autoReplyBox.Focus();
            _autoReplyBox.SelectAll();
        };
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "Monitor settings";
        AccessibleDescription = "Configure automation, rotation and model routing for the selected ChatGPT monitor.";
        _tabs.AccessibleName = "Monitor settings categories";
        _tabs.TabIndex = 0;

        ConfigureAccessible(_autoReplyBox, "Monitor auto reply", "Message sent after a stable assistant response.", 0);
        ConfigureAccessible(_delaySeconds, "Monitor reply delay", "Seconds to wait before sending the automatic reply.", 1);
        ConfigureAccessible(_timerSeconds, "Monitor polling timer", "Seconds between state checks for this monitor.", 2);
        ConfigureAccessible(_enabledCheck, "Monitor enabled", "Controls whether this saved monitor is eligible to run.", 3);

        ConfigureAccessible(_rotationEnabledCheck, "Conversation rotation enabled", "Allow this monitor to create a replacement chat when the current conversation reaches a supported rotation condition.", 0);
        ConfigureAccessible(_newChatMessageBox, "Context limit start message", "Message sent to the replacement chat after context-limit rotation.", 1);
        ConfigureAccessible(_newChatDelaySeconds, "New chat delay", "Seconds to wait before preparing a replacement conversation.", 2);
        ConfigureAccessible(_rotationCooldownSeconds, "Rotation cooldown", "Seconds to pause after a successful conversation handoff.", 3);
        ConfigureAccessible(_maxRotations, "Maximum rotations", "Maximum successful rotations for this monitor. Zero means unlimited.", 4);

        ConfigureAccessible(_modelRoutingEnabledCheck, "Model routing enabled", "Apply model routing only to newly-created rotation and recovery chats.", 0);
        ConfigureAccessible(_preferredModelBox, "Preferred model label", "Visible ChatGPT model label to try first. Auto keeps the current model.", 1);
        ConfigureAccessible(_fallbackModelBox, "Fallback model label", "Visible ChatGPT model label tried once if the preferred model is unavailable.", 2);

        _runtimeStatus.AccessibleName = "Monitor runtime status";
        _saveButton.AccessibleName = "Save monitor settings";
        _saveButton.TabIndex = 0;
        _cancelButton.AccessibleName = "Cancel monitor settings changes";
        _cancelButton.TabIndex = 1;
    }

    private static void ConfigureAccessible(Control control, string name, string description, int tabIndex)
    {
        control.AccessibleName = name;
        control.AccessibleDescription = description;
        control.TabIndex = tabIndex;
    }

    private void ApplyMonitorStatus(SavedMonitor monitor)
    {
        var running = monitor.RuntimeStatus.Contains("Running", StringComparison.OrdinalIgnoreCase);
        if (!monitor.Enabled)
        {
            _runtimeStatus.Text = "DISABLED";
            _runtimeStatus.ForeColor = FluentTheme.Muted;
            _runtimeStatus.BackColor = FluentTheme.SurfaceAlt;
            _runtimeStatus.AccessibleDescription = "This saved monitor is disabled.";
            return;
        }

        _runtimeStatus.Text = running ? "RUNNING" : "STOPPED";
        _runtimeStatus.ForeColor = running ? FluentTheme.Success : FluentTheme.Warning;
        _runtimeStatus.BackColor = running ? FluentTheme.SuccessSubtle : FluentTheme.WarningSubtle;
        _runtimeStatus.AccessibleDescription = running
            ? "This monitor is currently running."
            : "This monitor is currently stopped and can be edited.";
    }

    private void UpdateDependentControls()
    {
        var rotationEnabled = _rotationEnabledCheck.Checked;
        _newChatMessageBox.Enabled = rotationEnabled;
        _newChatDelaySeconds.Enabled = rotationEnabled;
        _rotationCooldownSeconds.Enabled = rotationEnabled;
        _maxRotations.Enabled = rotationEnabled;

        var routingEnabled = _modelRoutingEnabledCheck.Checked;
        _preferredModelBox.Enabled = routingEnabled;
        _fallbackModelBox.Enabled = routingEnabled;
    }

    private void TrySaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_autoReplyBox.Text))
        {
            _tabs.SelectedIndex = 0;
            _autoReplyBox.Focus();
            MessageBox.Show(this, "Auto reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_rotationEnabledCheck.Checked && string.IsNullOrWhiteSpace(_newChatMessageBox.Text))
        {
            _tabs.SelectedIndex = 1;
            _newChatMessageBox.Focus();
            MessageBox.Show(this, "Context-limit new Chat start message cannot be empty when rotation is enabled.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
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
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles[0] = new RowStyle(SizeType.Absolute, 32);
        layout.RowStyles[1] = new RowStyle(SizeType.Absolute, 34);
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

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control, string hint)
    {
        var labelBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        labelBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        labelBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        labelBlock.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0);
        labelBlock.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), AutoEllipsis = true }, 0, 1);
        root.Controls.Add(labelBlock, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(8, 7, 6, 7);
        root.Controls.Add(control, 1, row);
    }

    public static bool Edit(IWin32Window owner, SavedMonitor monitor)
    {
        using var dialog = new MonitorSettingsForm(monitor.Title, monitor.Url, monitor);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        monitor.AutoReply = dialog.AutoReply;
        monitor.ReplyDelaySeconds = dialog.ReplyDelaySeconds;
        monitor.TimerSeconds = dialog.TimerSeconds;
        monitor.Enabled = dialog.MonitorEnabled;
        monitor.ConversationRotationEnabled = dialog.ConversationRotationEnabled;
        monitor.NewChatStartMessage = dialog.NewChatStartMessage;
        monitor.NewChatDelaySeconds = dialog.NewChatDelaySeconds;
        monitor.RotationCooldownSeconds = dialog.RotationCooldownSeconds;
        monitor.MaxConversationRotations = dialog.MaxConversationRotations;
        monitor.ModelRoutingEnabled = dialog.ModelRoutingEnabled;
        monitor.PreferredModel = dialog.PreferredModel;
        monitor.FallbackModel = dialog.FallbackModel;
        return true;
    }
}
