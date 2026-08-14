using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Hooks SettingsForm once and stabilizes its tab surface after the async settings load finishes.
/// This intentionally avoids the previous per-idle repaint loop, which could cause visible blinking.
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
        state.Hooked = true;

        var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault();
        var status = Descendants(form).OfType<Label>()
            .FirstOrDefault(label => label.AccessibleRole == AccessibleRole.StatusBar);
        if (tabs is null || status is null) return;

        status.TextChanged += (_, _) =>
        {
            if (IsLoaded(status.Text))
                ScheduleOneShotStabilization(form, tabs, state);
        };

        tabs.EnabledChanged += (_, _) =>
        {
            if (tabs.Enabled && IsLoaded(status.Text))
                ScheduleOneShotStabilization(form, tabs, state);
        };

        form.VisibleChanged += (_, _) =>
        {
            if (!form.Visible) return;
            state.Stabilized = false;
            if (IsLoaded(status.Text))
                ScheduleOneShotStabilization(form, tabs, state);
        };

        if (IsLoaded(status.Text))
            ScheduleOneShotStabilization(form, tabs, state);
    }

    private static bool IsLoaded(string? status)
        => status?.StartsWith("Settings loaded", StringComparison.OrdinalIgnoreCase) == true;

    private static void ScheduleOneShotStabilization(SettingsForm form, TabControl tabs, RenderState state)
    {
        if (state.Scheduled || state.Stabilized || form.IsDisposed || form.Disposing) return;
        state.Scheduled = true;

        // Run after the current async-load/UI-style callbacks have drained. A second BeginInvoke
        // keeps this behind presentation hooks without introducing a timer or a recurring repaint.
        form.BeginInvoke(new Action(() =>
        {
            if (form.IsDisposed || form.Disposing) return;
            form.BeginInvoke(new Action(() =>
            {
                state.Scheduled = false;
                if (form.IsDisposed || form.Disposing || state.Stabilized) return;
                StabilizeLoadedSettings(form, tabs);
                state.Stabilized = true;
            }));
        }));
    }

    private static void StabilizeLoadedSettings(SettingsForm form, TabControl tabs)
    {
        form.SuspendLayout();
        tabs.SuspendLayout();
        try
        {
            tabs.Visible = true;
            tabs.Enabled = true;
            tabs.ForeColor = FluentTheme.Text;
            if (tabs.TabPages.Count > 0 && tabs.SelectedIndex < 0)
                tabs.SelectedIndex = 0;

            foreach (TabPage page in tabs.TabPages)
            {
                page.UseVisualStyleBackColor = false;
                page.Visible = true;
                page.BackColor = FluentTheme.Surface;
                page.ForeColor = FluentTheme.Text;

                foreach (var layout in Descendants(page).OfType<TableLayoutPanel>())
                {
                    layout.Visible = true;
                    layout.BackColor = FluentTheme.Surface;
                    layout.ForeColor = FluentTheme.Text;
                }

                foreach (var label in Descendants(page).OfType<Label>())
                {
                    label.Visible = true;
                    if (label.ForeColor == label.BackColor || IsSemanticStatusSurface(label.ForeColor))
                        label.ForeColor = label.Font.Bold ? FluentTheme.Text : FluentTheme.Muted;
                }

                foreach (var input in Descendants(page).Where(IsSettingsInput))
                    input.Visible = true;
            }
        }
        finally
        {
            tabs.ResumeLayout(performLayout: true);
            form.ResumeLayout(performLayout: true);
        }

        // Exactly one invalidation after load. Do not call Update/Refresh in an idle loop.
        tabs.Invalidate(invalidateChildren: true);
        form.Invalidate(invalidateChildren: true);
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
        internal bool Hooked { get; set; }
        internal bool Scheduled { get; set; }
        internal bool Stabilized { get; set; }
    }
}
