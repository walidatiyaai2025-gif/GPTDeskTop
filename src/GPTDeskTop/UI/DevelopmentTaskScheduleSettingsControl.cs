namespace GPTDeskTop.UI;

public sealed class DevelopmentTaskScheduleSettingsControl : UserControl
{
    private readonly NumericUpDown _work = new() { Minimum = 1, Maximum = 1440, Value = 30, Width = 120 };
    private readonly NumericUpDown _cooling = new() { Minimum = 1, Maximum = 1440, Value = 10, Width = 120 };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "" };
    private readonly DevelopmentTaskScheduleSettings _settings;

    public DevelopmentTaskScheduleSettingsControl(DevelopmentTaskScheduleSettings settings)
    {
        _settings = settings;
        Dock = DockStyle.Fill;
        Padding = new Padding(10);
        BuildUi();
        LoadSettings();
        _save.Click += (_, _) => SaveSettings();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.Controls.Add(new Label { Text = "Work window (minutes)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        root.Controls.Add(_work, 1, 0);
        root.Controls.Add(new Label { Text = "Cooling window (minutes)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        root.Controls.Add(_cooling, 1, 1);
        root.Controls.Add(new Label { Text = "Changes apply to the next Work/Cooling cycle.", AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 2);
        var note = root.GetControlFromPosition(0, 2);
        if (note is not null) root.SetColumnSpan(note, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.Add(_save);
        actions.Controls.Add(_status);
        root.Controls.Add(actions, 0, 3);
        root.SetColumnSpan(actions, 2);
        Controls.Add(root);
        FluentTheme.StyleButton(_save, primary: true);
    }

    private void LoadSettings()
    {
        try
        {
            _work.Value = Math.Clamp(_settings.WorkMinutes, (int)_work.Minimum, (int)_work.Maximum);
            _cooling.Value = Math.Clamp(_settings.CoolingMinutes, (int)_cooling.Minimum, (int)_cooling.Maximum);
            _status.Text = "Current settings loaded.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.WorkMinutes = (int)_work.Value;
            _settings.CoolingMinutes = (int)_cooling.Value;
            _settings.Save();
            _status.Text = "Saved. Changes apply to the next cycle.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }
}
