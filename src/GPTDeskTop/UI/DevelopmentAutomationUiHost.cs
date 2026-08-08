using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Adds the automation control surface to the existing MainForm without changing
/// its monitor grid architecture. This keeps the automation UI isolated from the
/// monitor workflow while still using the same service/database instances.
/// </summary>
public sealed class DevelopmentAutomationUiHost : IDisposable
{
    private readonly MainForm _mainForm;
    private readonly TaskAutomationService _automation;
    private readonly LocalDatabase _database;
    private Button? _button;

    public DevelopmentAutomationUiHost(MainForm mainForm, TaskAutomationService automation, LocalDatabase database)
    {
        _mainForm = mainForm;
        _automation = automation;
        _database = database;
        Install();
    }

    private void Install()
    {
        var toolbar = FindFirst<FlowLayoutPanel>(_mainForm);
        if (toolbar is null) return;

        _button = new Button { Text = "Development Automation", AutoSize = true };
        _button.Click += OpenAutomationForm;
        toolbar.Controls.Add(_button);
    }

    private void OpenAutomationForm(object? sender, EventArgs e)
    {
        using var form = new DevelopmentAutomationForm(_automation, _database);
        form.ShowDialog(_mainForm);
    }

    private static T? FindFirst<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            var nested = FindFirst<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    public void Dispose()
    {
        if (_button is null) return;
        _button.Click -= OpenAutomationForm;
        _button.Parent?.Controls.Remove(_button);
        _button.Dispose();
        _button = null;
    }
}
