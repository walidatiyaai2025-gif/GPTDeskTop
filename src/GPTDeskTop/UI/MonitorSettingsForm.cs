using GPTDeskTop.Models;

namespace GPTDeskTop.UI;

public sealed class MonitorSettingsForm : Form
{
    private readonly TextBox _autoReplyBox = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) };
    private readonly NumericUpDown _delaySeconds = new() { Minimum = 0, Maximum = 300, DecimalPlaces = 0, Width = 100 };
    private readonly NumericUpDown _timerSeconds = new() { Minimum = 1, Maximum = 60, DecimalPlaces = 0, Width = 100 };
    private readonly CheckBox _enabledCheck = new() { Text = "Enabled", AutoSize = true };

    public string AutoReply => _autoReplyBox.Text.Trim();
    public int ReplyDelaySeconds => (int)_delaySeconds.Value;
    public int TimerSeconds => (int)_timerSeconds.Value;
    public bool Enabled => _enabledCheck.Checked;

    public MonitorSettingsForm(string title, string url, string autoReply, int replyDelaySeconds, int timerSeconds, bool enabled)
    {
        Text = "Monitor Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 285);
        Font = new Font("Segoe UI", 9.5F);

        _autoReplyBox.Text = string.IsNullOrWhiteSpace(autoReply) ? "كمل" : autoReply;
        _delaySeconds.Value = Math.Clamp(replyDelaySeconds, 0, 300);
        _timerSeconds.Value = Math.Clamp(timerSeconds, 1, 60);
        _enabledCheck.Checked = enabled;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(new Label { Text = "Tab:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(titleLabel, 1, 0);

        root.Controls.Add(new Label { Text = "Auto reply:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(_autoReplyBox, 1, 1);

        root.Controls.Add(new Label { Text = "Delay before send (sec):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        root.Controls.Add(_delaySeconds, 1, 2);

        root.Controls.Add(new Label { Text = "Monitor timer (sec):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        root.Controls.Add(_timerSeconds, 1, 3);

        root.Controls.Add(new Label { Text = "State:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        root.Controls.Add(_enabledCheck, 1, 4);

        var urlLabel = new Label
        {
            Text = url,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.TopLeft
        };
        root.Controls.Add(new Label { Text = "URL:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft }, 0, 5);
        root.Controls.Add(urlLabel, 1, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(_autoReplyBox.Text))
            {
                MessageBox.Show(this, "Auto reply cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
            }
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 6);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
    }

    public static bool Edit(IWin32Window owner, SavedMonitor monitor)
    {
        using var dialog = new MonitorSettingsForm(
            monitor.Title,
            monitor.Url,
            monitor.AutoReply,
            monitor.ReplyDelaySeconds,
            monitor.TimerSeconds,
            monitor.Enabled);

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return false;

        monitor.AutoReply = dialog.AutoReply;
        monitor.ReplyDelaySeconds = dialog.ReplyDelaySeconds;
        monitor.TimerSeconds = dialog.TimerSeconds;
        monitor.Enabled = dialog.Enabled;
        return true;
    }
}
