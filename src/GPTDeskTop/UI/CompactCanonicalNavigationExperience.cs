using System.Reflection;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps the canonical project and field-diagnostics entry points visible after the compact
/// operator experience collapses the legacy grouped toolbar. The hidden MainForm buttons remain
/// the single behavior/event owner; these menu items only proxy their existing Click path.
/// </summary>
internal static class CompactCanonicalNavigationExperience
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += InstallWhenReady;

    private static void InstallWhenReady(object? sender, EventArgs e)
    {
        foreach (var main in Application.OpenForms.OfType<MainForm>().ToArray())
        {
            if (main.IsDisposed || main.Disposing)
                continue;

            if (!TryInstall(main))
                continue;

            // MainForm is single-instance for this process. Detach after the compact menu and
            // canonical source buttons both exist so this never becomes a lifetime UI scanner.
            Application.Idle -= InstallWhenReady;
            return;
        }
    }

    internal static bool TryInstall(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.IsDisposed || main.Disposing)
            return false;

        if (main.MainMenuStrip is not MenuStrip menu || menu.IsDisposed)
            return false;

        var buttons = Descendants(main).OfType<Button>().ToArray();
        var projectsButton = buttons.FirstOrDefault(button =>
            string.Equals(button.Text, "Projects", StringComparison.Ordinal));
        var runtimeInspectorButton = buttons.FirstOrDefault(button =>
            string.Equals(button.Text, "Runtime Inspector", StringComparison.Ordinal));

        if (projectsButton is null || runtimeInspectorButton is null)
            return false;

        EnsureVisibleProxy(
            menu,
            text: "Projects",
            source: projectsButton,
            insertIndex: 1,
            accessibleDescription: "Open the canonical Projects Hub.");
        EnsureVisibleProxy(
            menu,
            text: "Runtime Inspector",
            source: runtimeInspectorButton,
            insertIndex: 2,
            accessibleDescription: "Inspect the running GPTDeskTop build, monitor workers, browser processes and visible UI tree.");

        return true;
    }

    private static void EnsureVisibleProxy(
        MenuStrip menu,
        string text,
        Button source,
        int insertIndex,
        string accessibleDescription)
    {
        var existing = menu.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Visible = true;
            existing.Enabled = source.Enabled;
            return;
        }

        var item = new ToolStripMenuItem(text)
        {
            Tag = source,
            Enabled = source.Enabled,
            AccessibleName = text,
            AccessibleDescription = accessibleDescription,
            ToolTipText = accessibleDescription
        };
        item.Click += (_, _) => InvokeExistingButton(source);

        menu.Items.Insert(Math.Clamp(insertIndex, 0, menu.Items.Count), item);
    }

    private static void InvokeExistingButton(Button source)
    {
        if (source.IsDisposed || !source.Enabled)
            return;

        var onClick = source.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(EventArgs) },
            modifiers: null)
            ?? throw new MissingMethodException(source.GetType().FullName, "OnClick");

        try
        {
            onClick.Invoke(source, new object[] { EventArgs.Empty });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
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
