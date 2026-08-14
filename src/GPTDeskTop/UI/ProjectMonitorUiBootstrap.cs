using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class ProjectMonitorUiBootstrap
{
    private static readonly HashSet<nint> MainInjected = new();
    private static readonly HashSet<nint> SettingsInjected = new();
    private static ProjectMonitorDashboardForm? _dashboardForm;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TryInject();
    }

    private static void TryInject()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            try
            {
                if (form is MainForm main && main.IsHandleCreated && !main.IsDisposed && MainInjected.Add(main.Handle))
                    InjectMainButton(main);
                else if (form is SettingsForm settings && settings.IsHandleCreated && !settings.IsDisposed && SettingsInjected.Add(settings.Handle))
                    InjectSettingsTab(settings);
            }
            catch (Exception ex)
            {
                _ = ExceptionLogService.LogAsync(ex, "ProjectMonitorUiBootstrap.Inject");
            }
        }
    }

    private static void InjectMainButton(MainForm main)
    {
        var settingsButton = FindDescendants(main)
            .OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsButton?.Parent is null) return;
        if (FindDescendants(main).OfType<Button>().Any(b => string.Equals(b.Text, "Project Monitor", StringComparison.OrdinalIgnoreCase))) return;

        var button = new Button { Text = "Project Monitor", AutoSize = true, AccessibleName = "Open project monitor dashboard" };
        FluentTheme.StyleButton(button, primary: true);
        button.Click += (_, _) => ShowDashboard(main);
        settingsButton.Parent.Controls.Add(button);
        var settingsIndex = settingsButton.Parent.Controls.GetChildIndex(settingsButton);
        settingsButton.Parent.Controls.SetChildIndex(button, Math.Max(0, settingsIndex));
    }

    private static void InjectSettingsTab(SettingsForm settings)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var tabs = typeof(SettingsForm).GetField("_tabs", flags)?.GetValue(settings) as TabControl;
        if (tabs is null || tabs.TabPages.Cast<TabPage>().Any(p => string.Equals(p.Text, "Project Monitor", StringComparison.OrdinalIgnoreCase))) return;
        var page = new TabPage("Project Monitor") { BackColor = FluentTheme.Background, Padding = new Padding(8) };
        var dashboard = new ProjectMonitorDashboardControl { Dock = DockStyle.Fill };
        page.Controls.Add(dashboard);
        tabs.TabPages.Add(page);
        _ = dashboard.RefreshAsync();
    }

    private static void ShowDashboard(IWin32Window owner)
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = new ProjectMonitorDashboardForm();
            _dashboardForm.FormClosed += (_, _) => _dashboardForm = null;
            _dashboardForm.Show(owner);
        }
        else
        {
            if (_dashboardForm.WindowState == FormWindowState.Minimized) _dashboardForm.WindowState = FormWindowState.Normal;
            _dashboardForm.BringToFront();
            _dashboardForm.Activate();
        }
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child)) yield return descendant;
        }
    }
}
