using GPTDeskTop.Models;

namespace GPTDeskTop.UI;

public sealed class MonitorSettingsForm : Form
{
    private readonly TextBox _autoReplyBox = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _delaySeconds = new() { Minimum = 0, Maximum = 300, Width = 110 };
    private readonly NumericUpDown _timerSeconds = new() { Minimum = 1, Maximum = 60, Width = 110 };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", AutoSize = true };

    public string AutoReply => _autoReplyBox.Text.Trim();
    public int ReplyDelaySeconds => (int)_delaySeconds.Value;
    public int TimerSeconds => (int)_timerSeconds.Value;
    public bool MonitorEnabled => _enabledCheck.Checked;

    public MonitorSettingsForm(string title, string url, string autoReply, int replyDelaySeconds, int timerSeconds, bool enabled)
    {
        Text = "Monitor Settings"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(650, 315);
        _autoReplyBox.Text = string.IsNullOrWhiteSpace(autoReply) ? "كمل" : autoReply;
        _delaySeconds.Value = Math.Clamp(replyDelaySeconds, 0, 300); _timerSeconds.Value = Math.Clamp(timerSeconds, 1, 60); _enabledCheck.Checked = enabled;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var titleLabel = new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        AddRow(root, 0, "Tab", titleLabel); AddRow(root, 1, "Auto reply", _autoReplyBox); AddRow(root, 2, "Delay before send (sec)", _delaySeconds); AddRow(root, 3, "Monitor timer (sec)", _timerSeconds); AddRow(root, 4, "State", _enabledCheck);
        var urlLabel = new Label { Text = url, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.TopLeft };
        AddRow(root, 5, "URL", urlLabel);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save Monitor", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_autoReplyBox.Text)) return; MessageBox.Show(this, "Auto reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.None; };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 6); root.SetColumnSpan(buttons, 2);
        Controls.Add(root); AcceptButton = save; CancelButton = cancel;
        FluentTheme.Apply(this); FluentTheme.StyleButton(save, primary: true);
    }

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control)
    {
        root.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = FluentTheme.Muted }, 0, row);
        control.Anchor = row == 5 ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Right;
        root.Controls.Add(control, 1, row);
    }

    public static bool Edit(IWin32Window owner, SavedMonitor monitor)
    {
        using var dialog = new MonitorSettingsForm(monitor.Title, monitor.Url, monitor.AutoReply, monitor.ReplyDelaySeconds, monitor.TimerSeconds, monitor.Enabled);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        monitor.AutoReply = dialog.AutoReply; monitor.ReplyDelaySeconds = dialog.ReplyDelaySeconds; monitor.TimerSeconds = dialog.TimerSeconds; monitor.Enabled = dialog.MonitorEnabled;
        return true;
    }
}
