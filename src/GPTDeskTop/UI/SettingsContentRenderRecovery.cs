using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps the Settings TabControl enabled while asynchronous settings I/O is running.
/// Disabling a WinForms TabControl can hand child painting to the disabled visual-style path,
/// which caused the visible blank/flicker regression on some Windows/DPI combinations.
/// Busy state is represented by the status text/cursor and action buttons instead.
/// </summary>
internal static class SettingsContentRenderRecoveryBootstrap
{
    private static readonly ConditionalWeakTable<SettingsForm, RenderState> States = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += DiscoverSettingsForms;

    private static void DiscoverSettingsForms(object? sender, EventArgs e)
    {
        if (Application.OpenForms.Count == 0) return;

        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
            EnsureHooked(form);
    }

    private static void EnsureHooked(SettingsForm form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var state = States.GetValue(form, _ => new RenderState());
        if (state.Hooked) return;

        var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault();
        if (tabs is null) return;

        state.Hooked = true;

        // Root fix: never let the settings content surface enter WinForms' disabled TabControl
        // painting path. SetBusy still disables Save/Import/Export and updates the wait cursor.
        tabs.EnabledChanged += (_, _) => KeepSettingsTabsEnabled(tabs);
        KeepSettingsTabsEnabled(tabs);

        form.VisibleChanged += (_, _) =>
        {
            if (form.Visible)
                KeepSettingsTabsEnabled(tabs);
        };
    }

    private static void KeepSettingsTabsEnabled(TabControl tabs)
    {
        if (tabs.IsDisposed || tabs.Disposing || tabs.Enabled) return;
        tabs.Enabled = true;
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

    private sealed class RenderState
    {
        internal bool Hooked { get; set; }
    }
}
