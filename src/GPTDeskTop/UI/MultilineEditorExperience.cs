using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Keeps long-message editors visually multiline after all secondary-screen and DPI layout passes.
/// Some legacy TableLayoutPanel helpers reapply horizontal-only anchoring after construction; this
/// guard restores the intended vertical fill without changing the stored settings or dialog logic.
/// </summary>
internal static class MultilineEditorExperience
{
    private static readonly ConditionalWeakTable<Form, AppliedMarker> Applied = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += ApplyToOpenSettingsForms;

    private static void ApplyToOpenSettingsForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.IsDisposed || form.Disposing)
                continue;
            if (form is not MonitorSettingsForm && form is not SettingsForm && form is not NewChatMonitorForm)
                continue;
            if (Applied.TryGetValue(form, out _))
                continue;

            Apply(form);
            Applied.Add(form, new AppliedMarker());
        }
    }

    internal static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        foreach (var textBox in Descendants(form).OfType<TextBox>().Where(box => box.Multiline))
        {
            textBox.Multiline = true;
            textBox.AcceptsReturn = true;
            textBox.WordWrap = true;
            textBox.ScrollBars = ScrollBars.Vertical;
            textBox.AutoSize = false;
            textBox.Dock = DockStyle.Fill;
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBox.MinimumSize = new Size(0, 72);

            if (textBox.Parent is not TableLayoutPanel layout)
                continue;

            var row = layout.GetRow(textBox);
            if (row < 0 || row >= layout.RowStyles.Count)
                continue;

            var style = layout.RowStyles[row];
            if (style.SizeType == SizeType.Absolute && style.Height < 96F)
                style.Height = 96F;
        }

        form.PerformLayout();
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

    private sealed class AppliedMarker
    {
    }
}
