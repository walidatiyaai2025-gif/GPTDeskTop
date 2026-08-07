using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

public sealed class SettingsForm : Form
{
    private readonly LocalDatabase _database;
    private readonly NumericUpDown _replyDelay = new()
    {
        Minimum = 0,
        Maximum = 300,
        DecimalPlaces = 0,
        Width = 120
    };
    private readonly NumericUpDown _notificationDuration = new()
    {
        Minimum = 1,
        Maximum = 60,
        DecimalPlaces = 0,
        Width = 120
    };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public int NotificationDurationSeconds => (int)_notificationDuration.Value;

    public SettingsForm(LocalDatabase database)
    {
        _database = database;
        Text = "GPTDeskTop Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 190);
        Font = new Font("Segoe UI", 10F);

        BuildUi();
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
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "Delay before sending auto reply (seconds)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        root.Controls.Add(_replyDelay, 1, 0);

        root.Controls.Add(new Label
        {
            Text = "Balloon notification duration (seconds)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        root.Controls.Add(_notificationDuration, 1, 1);

        root.Controls.Add(new Label
        {
            Text = "Reply delay applies to every monitor and is read before every send.",
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Fill,
            AutoSize = true
        }, 0, 2);
        root.SetColumnSpan(root.GetControlFromPosition(0, 2)!, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);
        root.Controls.Add(buttons, 0, 3);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
    }

    private async Task LoadSettingsAsync()
    {
        _replyDelay.Value = await _database.GetIntSettingAsync("ReplyDelaySeconds", 3, 0, 300);
        _notificationDuration.Value = await _database.GetIntSettingAsync("NotificationDurationSeconds", 8, 1, 60);
    }

    private async Task SaveSettingsAsync()
    {
        await _database.SetSettingAsync("ReplyDelaySeconds", ((int)_replyDelay.Value).ToString());
        await _database.SetSettingAsync("NotificationDurationSeconds", ((int)_notificationDuration.Value).ToString());
        DialogResult = DialogResult.OK;
        Close();
    }
}
