using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Makes Projects Hub the one user-facing monitor/project navigation surface.
/// Legacy monitor CRUD commands remain implemented in MainForm for compatibility and recovery,
/// but are removed from the compact operator menu after the canonical Projects entry is available.
/// </summary>
internal static class ProjectsHubNavigationConsolidation
{
    private static readonly HashSet<nint> Consolidated = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => TryConsolidateOpenMainForms();

    private static void TryConsolidateOpenMainForms()
    {
        foreach (var main in Application.OpenForms.OfType<MainForm>().ToArray())
        {
            if (!main.IsHandleCreated || main.IsDisposed || main.Disposing || Consolidated.Contains(main.Handle))
                continue;

            try
            {
                if (TryConsolidate(main))
                    Consolidated.Add(main.Handle);
            }
            catch (Exception ex)
            {
                _ = ExceptionLogService.LogAsync(ex, "ProjectsHubNavigationConsolidation");
            }
        }
    }

    internal static bool TryConsolidate(MainForm main)
    {
        var projectsButton = Descendants(main)
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Text, "Projects", StringComparison.OrdinalIgnoreCase));
        if (projectsButton is null)
            return false;

        var root = Descendants(main)
            .OfType<MenuStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripMenuItem>())
            .FirstOrDefault(item => string.Equals(item.Text, "☰ Commands", StringComparison.Ordinal));
        if (root is null)
            return false;

        var obsoleteMonitorMenu = root.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Monitors", StringComparison.OrdinalIgnoreCase));
        if (obsoleteMonitorMenu is not null)
        {
            root.DropDownItems.Remove(obsoleteMonitorMenu);
            obsoleteMonitorMenu.Dispose();
        }

        if (!root.DropDownItems.OfType<ToolStripMenuItem>()
            .Any(item => string.Equals(item.Text, "Projects Hub", StringComparison.OrdinalIgnoreCase)))
        {
            var projectsHub = new ToolStripMenuItem("Projects Hub")
            {
                Tag = projectsButton,
                ToolTipText = "Open the canonical Projects Hub for project creation, monitor execution, state, tasks and results."
            };
            projectsHub.Click += (_, _) => InvokeButton(projectsButton);
            root.DropDownItems.Insert(0, projectsHub);
        }

        return true;
    }

    private static void InvokeButton(Button source)
    {
        if (source.IsDisposed || !source.Enabled)
            return;

        try
        {
            var onClick = source.GetType().GetMethod(
                "OnClick",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);
            if (onClick is null)
                throw new MissingMethodException(source.GetType().FullName, "OnClick");
            onClick.Invoke(source, new object[] { EventArgs.Empty });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionLogService.Log(ex.InnerException, "ProjectsHubNavigationConsolidation.OpenProjects");
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "ProjectsHubNavigationConsolidation.OpenProjects");
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
