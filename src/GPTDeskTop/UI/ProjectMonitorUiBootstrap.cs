using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class ProjectMonitorUiBootstrap
{
    private static readonly HashSet<nint> MainInjected = new();
    private static ProjectMonitorDashboardForm? _dashboardForm;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TryInstallProjectsEntry();
    }

    private static void TryInstallProjectsEntry()
    {
        foreach (var main in Application.OpenForms.OfType<MainForm>().ToArray())
        {
            try
            {
                if (!main.IsHandleCreated || main.IsDisposed || main.Disposing || !MainInjected.Add(main.Handle))
                    continue;

                ConfigureRuntimeContext(main);
                InjectProjectsButton(main);
            }
            catch (Exception ex)
            {
                _ = ExceptionLogService.LogAsync(ex, "ProjectMonitorUiBootstrap.InstallProjectsEntry");
            }
        }
    }

    private static void InjectProjectsButton(MainForm main)
    {
        var settingsButton = FindDescendants(main)
            .OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsButton?.Parent is null) return;
        if (FindDescendants(main).OfType<Button>().Any(b => string.Equals(b.Text, "Projects", StringComparison.OrdinalIgnoreCase))) return;

        var button = new Button
        {
            Text = "Projects",
            AutoSize = true,
            AccessibleName = "Open Projects Hub"
        };
        FluentTheme.StyleButton(button, primary: true);
        button.Click += (_, _) => ShowProjectsHub(main);
        settingsButton.Parent.Controls.Add(button);
        var settingsIndex = settingsButton.Parent.Controls.GetChildIndex(settingsButton);
        settingsButton.Parent.Controls.SetChildIndex(button, Math.Max(0, settingsIndex));
    }

    private static void ConfigureRuntimeContext(MainForm main)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var database = typeof(MainForm).GetField("_database", flags)?.GetValue(main) as LocalDatabase;
        var monitor = typeof(MainForm).GetField("_monitor", flags)?.GetValue(main) as ChatGptMonitorService;
        var chrome = typeof(MainForm).GetField("_chrome", flags)?.GetValue(main) as ChromeDevToolsService;
        if (database is not null && monitor is not null && chrome is not null)
            ProjectExecutionRuntimeContext.Configure(database, monitor, chrome);
    }

    private static void ShowProjectsHub(MainForm owner)
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = new ProjectMonitorDashboardForm(() => StartNewProjectMonitorAsync(owner));
            _dashboardForm.FormClosed += (_, _) => _dashboardForm = null;
            _dashboardForm.Show(owner);
        }
        else
        {
            if (_dashboardForm.WindowState == FormWindowState.Minimized)
                _dashboardForm.WindowState = FormWindowState.Normal;
            _dashboardForm.BringToFront();
            _dashboardForm.Activate();
        }
    }

    private static async Task StartNewProjectMonitorAsync(MainForm owner)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var method = typeof(MainForm).GetMethod("CreateNewChatMonitorAsync", flags)
            ?? throw new MissingMethodException(nameof(MainForm), "CreateNewChatMonitorAsync");
        if (method.Invoke(owner, null) is Task task)
            await task;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
    }
}
