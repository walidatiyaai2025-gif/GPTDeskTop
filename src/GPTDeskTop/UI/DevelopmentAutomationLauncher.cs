using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Adds the development-automation control center to the existing MainForm
/// without coupling MainForm to the automation engine.
/// </summary>
public static class DevelopmentAutomationLauncher
{
    private const string ButtonName = "DevelopmentAutomationButton";

    public static void Attach(Form mainForm, TaskAutomationService automation, LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(database);

        if (FindControl(mainForm, ButtonName) is not null)
            return;

        var toolbar = FindFirst<FlowLayoutPanel>(mainForm, panel =>
            panel.Controls.OfType<Button>().Any(button =>
                string.Equals(button.Text, "Launch Chrome", StringComparison.OrdinalIgnoreCase)));

        if (toolbar is null)
            return;

        var button = new Button
        {
            Name = ButtonName,
            Text = "Development Automation",
            AutoSize = true,
            AccessibleName = "Development Task Automation"
        };

        FluentTheme.StyleButton(button, primary: true);
        button.Click += (_, _) =>
        {
            using var form = new DevelopmentAutomationControlForm(automation, database);
            form.ShowDialog(mainForm);
        };

        toolbar.Controls.Add(button);
        toolbar.Controls.SetChildIndex(button, 0);
    }

    private static Control? FindControl(Control root, string name)
    {
        foreach (Control child in root.Controls)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
                return child;

            var nested = FindControl(child, name);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static T? FindFirst<T>(Control root, Func<T, bool> predicate) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed && predicate(typed))
                return typed;

            var nested = FindFirst(child, predicate);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
