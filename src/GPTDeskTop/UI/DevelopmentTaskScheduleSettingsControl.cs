using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.UI;

public sealed class DevelopmentTaskScheduleSettingsControl : UserControl
{
    private readonly DevelopmentTaskScheduleSettingsStore _store;
    private readonly NumericUpDown _work = new() { Minimum = 1, Maximum = 120, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly NumericUpDown _cooling = new() { Minimum = 1, Maximum = 120, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "Save Schedule", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };

    public DevelopmentTaskScheduleSettingsControl(string? settingsPath = null)
    {
        _store = new DevelopmentTaskScheduleSettingsStore(settingsPath);
        Name = "DevelopmentScheduleSettings";
        AccessibleName = "Development task schedule settings";
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Surface;
        Padding = new Padding(2);
        BuildUi();
        LoadSettings();
        _save.Click += (_, _) => SaveSettings();
        FluentTheme.StyleButton(_save, primary: true);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(2),
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateLabel("Work window (minutes)"), 0, 0);
        root.Controls.Add(_work, 1, 0);
        root.Controls.Add(CreateLabel("Cooling window (minutes)"), 0, 1);
        root.Controls.Add(_cooling, 1, 1);
        var note = FluentTheme.CreateMutedLabel("Changes take effect through the existing schedule store on the next Work/Cooling cycle.");
        root.Controls.Add(note, 0, 2);
        root.SetColumnSpan(note, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.Controls.Add(_save, 0, 0);
        actions.Controls.Add(_status, 1, 0);
        root.Controls.Add(actions, 0, 3);
        root.SetColumnSpan(actions, 2);
        Controls.Add(root);
    }

    private static Label CreateLabel(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Text,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

    private void LoadSettings()
    {
        try
        {
            var settings = _store.Load();
            _work.Value = settings.WorkMinutes;
            _cooling.Value = settings.CoolingMinutes;
            _status.Text = "Loaded from the canonical schedule store.";
            _status.ForeColor = FluentTheme.Muted;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = FluentTheme.Danger;
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
            _status.Text = $"Saved {DateTime.Now:t} — next cycle will use these values.";
            _status.ForeColor = FluentTheme.Success;
        }
        catch (Exception ex)
        {
            _status.Text = "Save failed.";
            _status.ForeColor = FluentTheme.Danger;
            MessageBox.Show(FindForm(), ex.Message, "Development Schedule", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
