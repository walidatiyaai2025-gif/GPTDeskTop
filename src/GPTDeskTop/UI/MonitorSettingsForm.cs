using GPTDeskTop.Models;

namespace GPTDeskTop.UI;

public sealed class MonitorSettingsForm : Form
{
    private readonly TextBox _autoReplyBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _delaySeconds = new() { Minimum = 0, Maximum = 300, Width = 110 };
    private readonly NumericUpDown _timerSeconds = new() { Minimum = 1, Maximum = 60, Width = 110 };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _rotationEnabledCheck = new() { Text = "Open a new Chat when ChatGPT reports a conversation/context limit", AutoSize = true };
    private readonly TextBox _newChatMessageBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _newChatDelaySeconds = new() { Minimum = 0, Maximum = 600, Width = 110 };
    private readonly NumericUpDown _rotationCooldownSeconds = new() { Minimum = 0, Maximum = 3600, Width = 110 };
    private readonly NumericUpDown _maxRotations = new() { Minimum = 0, Maximum = 1000, Width = 110 };

    public string AutoReply => _autoReplyBox.Text.Trim();
    public int ReplyDelaySeconds => (int)_delaySeconds.Value;
    public int TimerSeconds => (int)_timerSeconds.Value;
    public bool MonitorEnabled => _enabledCheck.Checked;
    public bool ConversationRotationEnabled => _rotationEnabledCheck.Checked;
    public string NewChatStartMessage => _newChatMessageBox.Text.Trim();
    public int NewChatDelaySeconds => (int)_newChatDelaySeconds.Value;
    public int RotationCooldownSeconds => (int)_rotationCooldownSeconds.Value;
    public int MaxConversationRotations => (int)_maxRotations.Value;

    public MonitorSettingsForm(string title, string url, SavedMonitor monitor)
    {
        Text = "Monitor Settings"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(760, 540);

        _autoReplyBox.Text = string.IsNullOrWhiteSpace(monitor.AutoReply) ? "كمل" : monitor.AutoReply;
        _delaySeconds.Value = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        _timerSeconds.Value = Math.Clamp(monitor.TimerSeconds, 1, 60);
        _enabledCheck.Checked = monitor.Enabled;
        _rotationEnabledCheck.Checked = monitor.ConversationRotationEnabled;
        _newChatMessageBox.Text = string.IsNullOrWhiteSpace(monitor.NewChatStartMessage) ? "كمل" : monitor.NewChatStartMessage;
        _newChatDelaySeconds.Value = Math.Clamp(monitor.NewChatDelaySeconds, 0, 600);
        _rotationCooldownSeconds.Value = Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600);
        _maxRotations.Value = Math.Clamp(monitor.MaxConversationRotations, 0, 1000);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 12, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 11; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var titleLabel = new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        AddRow(root, 0, "Tab", titleLabel); AddRow(root, 1, "Auto reply", _autoReplyBox); AddRow(root, 2, "Delay before send (sec)", _delaySeconds); AddRow(root, 3, "Monitor timer (sec)", _timerSeconds); AddRow(root, 4, "State", _enabledCheck);
        AddRow(root, 5, "Conversation rotation", _rotationEnabledCheck); AddRow(root, 6, "New Chat start message", _newChatMessageBox); AddRow(root, 7, "New Chat delay (sec)", _newChatDelaySeconds);
        AddRow(root, 8, "Rotation cooldown (sec)", _rotationCooldownSeconds); AddRow(root, 9, "Max rotations (0 = unlimited)", _maxRotations);
        var urlLabel = new Label { Text = url, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        AddRow(root, 10, "URL", urlLabel);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save Monitor", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_autoReplyBox.Text)) { MessageBox.Show(this, "Auto reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.None; return; }
            if (_rotationEnabledCheck.Checked && string.IsNullOrWhiteSpace(_newChatMessageBox.Text)) { MessageBox.Show(this, "New Chat start message cannot be empty when rotation is enabled.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.None; }
        };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 11); root.SetColumnSpan(buttons, 2);
        Controls.Add(root); AcceptButton = save; CancelButton = cancel;
        FluentTheme.Apply(this); FluentTheme.StyleButton(save, primary: true);
    }

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control)
    {
        root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = FluentTheme.Muted }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
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
        return true;
    }
}
