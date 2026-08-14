using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps the application-settings tab surface owned by SettingsForm after global presentation
/// passes and after the temporary disabled state used while settings are loading.
/// </summary>
internal static class SettingsContentRenderRecoveryBootstrap
{
    private static readonly ConditionalWeakTable<SettingsForm, RenderState> States = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += OnApplicationIdle;

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        if (Application.OpenForms.Count == 0) return;

        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
            Recover(form);
    }

    internal static void Recover(SettingsForm form)
    {
        if (form.IsDisposed || form.Disposing || !form.IsHandleCreated) return;

        var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault();
        if (tabs is null) return;

        var status = Descendants(form).OfType<Label>()
            .FirstOrDefault(label => label.AccessibleRole == AccessibleRole.StatusBar);
        var loaded = status?.Text.StartsWith("Settings loaded", StringComparison.OrdinalIgnoreCase) == true;

        var state = States.GetValue(form, _ => new RenderState());
        var enabledTransition = tabs.Enabled && !state.TabsWereEnabled;
        var loadedTransition = loaded && !state.SettingsWereLoaded;
        var contaminated = HasContaminatedSettingsSurface(tabs);

        if (enabledTransition || loadedTransition || contaminated)
            NormalizeAndRepaint(form, tabs);

        state.TabsWereEnabled = tabs.Enabled;
        state.SettingsWereLoaded = loaded;
    }

    private static bool HasContaminatedSettingsSurface(TabControl tabs)
        => tabs.TabPages.Cast<TabPage>().Any(page =>
            IsSemanticStatusSurface(page.BackColor)
            || Descendants(page).OfType<TableLayoutPanel>().Any(layout => IsSemanticStatusSurface(layout.BackColor)));

    private static void NormalizeAndRepaint(SettingsForm form, TabControl tabs)
    {
        tabs.ForeColor = FluentTheme.Text;

        foreach (TabPage page in tabs.TabPages)
        {
            // Do not allow OS visual styles or a semantic-status pass to own the settings content surface.
            page.UseVisualStyleBackColor = false;
            page.BackColor = FluentTheme.Surface;
            page.ForeColor = FluentTheme.Text;

            foreach (var layout in Descendants(page).OfType<TableLayoutPanel>())
            {
                if (IsSemanticStatusSurface(layout.BackColor))
                    layout.BackColor = FluentTheme.Surface;
                layout.ForeColor = FluentTheme.Text;
            }

            foreach (var label in Descendants(page).OfType<Label>())
            {
                label.Visible = true;
                if (label.AccessibleRole == AccessibleRole.StatusBar) continue;

                // Preserve explicit warning/danger/accent colors. Only repair labels that became
                // unreadable because foreground and surface collapsed onto the same/subtle color.
                if (label.ForeColor == label.BackColor || IsSemanticStatusSurface(label.ForeColor))
                    label.ForeColor = label.Font.Bold ? FluentTheme.Text : FluentTheme.Muted;
            }

            foreach (var input in Descendants(page).Where(IsSettingsInput))
                input.Visible = true;

            page.PerformLayout();
            page.Invalidate(invalidateChildren: true);
        }

        tabs.PerformLayout();
        tabs.Invalidate(invalidateChildren: true);
        form.PerformLayout();
        form.Invalidate(invalidateChildren: true);
        form.Update();
    }

    private static bool IsSettingsInput(Control control)
        => control is TextBoxBase
           or NumericUpDown
           or ComboBox
           or CheckBox
           or Button;

    private static bool IsSemanticStatusSurface(Color color)
        => color == FluentTheme.SuccessSubtle
           || color == FluentTheme.InfoSubtle
           || color == FluentTheme.WarningSubtle
           || color == FluentTheme.DangerSubtle;

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
        internal bool TabsWereEnabled { get; set; }
        internal bool SettingsWereLoaded { get; set; }
    }
}
