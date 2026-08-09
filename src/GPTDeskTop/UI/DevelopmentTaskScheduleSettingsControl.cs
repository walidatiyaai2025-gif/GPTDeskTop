using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

public sealed class DevelopmentTaskScheduleSettingsControl : UserControl
{
    private readonly DevelopmentTaskScheduleSettingsStore _store;
    private readonly NumericUpDown _work = new() { Minimum = 1, Maximum = 120, DecimalPlaces = 0 };
    private readonly NumericUpDown _cooling = new() { Minimum = 1, Maximum = 120, DecimalPlaces = 0 };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };

    public DevelopmentTaskScheduleSettingsControl(string? settingsPath = null)
    {
        _store = new DevelopmentTaskScheduleSettingsStore(settingsPath);
        Dock = DockStyle.Fill;
        Padding = new Padding(20);
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
        var note = new Label { Text = "Changes apply to the next Work/Cooling cycle.", AutoSize = true, ForeColor = SystemColors.GrayText };
        root.Controls.Add(note, 0, 2);
        root.SetColumnSpan(note, 2);
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
            var settings = _store.Load();
            _work.Value = settings.WorkMinutes;
            _cooling.Value = settings.CoolingMinutes;
            _status.Text = "Loaded";
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
            _store.Save(new DevelopmentTaskScheduleSettings
            {
                WorkMinutes = (int)_work.Value,
                CoolingMinutes = (int)_cooling.Value
            });
            _status.Text = "Saved — next cycle will use the new values.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, "Development Schedule", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
