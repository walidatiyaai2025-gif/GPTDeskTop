using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class LayoutStabilityBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => LayoutStability.ApplyOpenForms();
}

/// <summary>
/// Incremental, presentation-only WinForms layout hardening. Existing forms are
/// traversed once, dynamically added controls are hardened through ControlAdded,
/// and responsive state is reapplied only on resize/DPI changes.
/// </summary>
public static class LayoutStability
{
    private static readonly ConditionalWeakTable<Form, FormRegistration> Forms = new();
    private static readonly ConditionalWeakTable<Control, ControlRegistration> Controls = new();

    public static void ApplyOpenForms()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            Apply(form);
    }

    public static void Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var registration = Forms.GetValue(form, _ => new FormRegistration());
        if (registration.Initialized) return;
        registration.Initialized = true;

        registration.ToolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 450,
            ReshowDelay = 120,
            ShowAlways = true
        };

        if (form.AutoScaleMode == AutoScaleMode.None)
            form.AutoScaleMode = AutoScaleMode.Dpi;

        if (form.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow)
        {
            form.MinimumSize = new Size(
                Math.Max(form.MinimumSize.Width, Scale(form, LayoutTokens.MinimumUsableWidth)),
                Math.Max(form.MinimumSize.Height, Scale(form, LayoutTokens.MinimumUsableHeight)));
        }

        form.Resize += (_, _) => ApplyResponsiveState(form);
        form.DpiChanged += (_, _) => ApplyResponsiveState(form);
        form.FormClosed += (_, _) =>
        {
            registration.ToolTip?.Dispose();
            registration.ToolTip = null;
        };

        HardenTree(form, form, registration);
        ApplyResponsiveState(form);
    }

    private static void HardenTree(Form form, Control control, FormRegistration formRegistration)
    {
        if (control.IsDisposed) return;

        var registration = Controls.GetValue(control, _ => new ControlRegistration());
        if (!registration.ChildHooked)
        {
            registration.ChildHooked = true;
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is null || e.Control.IsDisposed || form.IsDisposed) return;
                HardenTree(form, e.Control, formRegistration);
                ApplyResponsiveState(form);
            };
        }

        HardenControl(control, formRegistration);

        foreach (Control child in control.Controls)
            HardenTree(form, child, formRegistration);
    }

    private static void HardenControl(Control control, FormRegistration formRegistration)
    {
        switch (control)
        {
            case Label label:
                HardenLabel(label, formRegistration);
                break;
            case Button button:
                HardenButton(button, formRegistration);
                break;
            case TextBox textBox:
                HardenTextBox(textBox);
                break;
            case ComboBox comboBox:
                HardenInput(comboBox);
                comboBox.DropDownWidth = Math.Max(comboBox.DropDownWidth, comboBox.Width);
                break;
            case NumericUpDown numeric:
                HardenInput(numeric);
                break;
            case DateTimePicker picker:
                HardenInput(picker);
                break;
            case FlowLayoutPanel flow:
                HardenFlow(flow);
                break;
            case TableLayoutPanel table:
                HardenTable(table);
                break;
            case TabPage page:
                HardenPage(page);
                break;
            case SplitContainer split:
                HardenSplit(split);
                break;
            case DataGridView grid:
                HardenGrid(grid);
                break;
            case RichTextBox rich:
                HardenRichText(rich);
                break;
        }
    }

    private static void HardenLabel(Label label, FormRegistration registration)
    {
        var constrained = label.Dock != DockStyle.None
                          || (label.Anchor.HasFlag(AnchorStyles.Left) && label.Anchor.HasFlag(AnchorStyles.Right))
                          || !label.AutoSize;

        if (constrained)
            label.AutoEllipsis = true;

        HookLongTextTooltip(label, registration);
    }

    private static void HardenButton(Button button, FormRegistration registration)
    {
        button.MinimumSize = new Size(
            button.MinimumSize.Width,
            Math.Max(button.MinimumSize.Height, Scale(button, LayoutTokens.ControlHeight)));

        if (button.Margin == Padding.Empty)
            button.Margin = Scale(button, LayoutTokens.ControlMargin);

        // Ellipsis is a last-resort safety net. UI-POLISH-006 already gives critical
        // main/runtime/development actions enough width, so they still render in full.
        if (NeedsSingleLineEllipsis(button))
            button.AutoEllipsis = true;

        HookLongTextTooltip(button, registration);
    }

    private static bool NeedsSingleLineEllipsis(Button button)
    {
        var text = button.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || button.AutoSize) return false;

        var measured = TextRenderer.MeasureText(text, button.Font, Size.Empty, TextFormatFlags.SingleLine).Width;
        var available = Math.Max(button.Width, button.MinimumSize.Width) - button.Padding.Horizontal - Scale(button, LayoutTokens.Space8);
        return measured > Math.Max(1, available);
    }

    private static void HardenTextBox(TextBox box)
    {
        if (!box.Multiline)
        {
            box.MinimumSize = new Size(
                box.MinimumSize.Width,
                Math.Max(box.MinimumSize.Height, Scale(box, LayoutTokens.CompactControlHeight)));
        }

        if (box.Multiline && box.ScrollBars == ScrollBars.None && (box.ReadOnly || box.AcceptsReturn))
            box.ScrollBars = ScrollBars.Vertical;
    }

    private static void HardenInput(Control control)
    {
        control.MinimumSize = new Size(
            control.MinimumSize.Width,
            Math.Max(control.MinimumSize.Height, Scale(control, LayoutTokens.CompactControlHeight)));
    }

    private static void HardenFlow(FlowLayoutPanel flow)
    {
        if (!flow.Controls.OfType<Button>().Any()) return;

        if (flow.Margin == Padding.Empty)
            flow.Margin = Scale(flow, LayoutTokens.ControlMargin);

        flow.AutoScroll = false;
    }

    private static void HardenTable(TableLayoutPanel table)
    {
        if (table.Margin == Padding.Empty)
            table.Margin = Scale(table, LayoutTokens.ControlMargin);

        foreach (Control child in table.Controls)
        {
            if (child.Dock == DockStyle.Fill && child.Margin == Padding.Empty)
                child.Margin = Scale(child, LayoutTokens.ControlMargin);
        }
    }

    private static void HardenPage(TabPage page)
    {
        page.AutoScroll = true;
        page.AutoScrollMargin = new Size(Scale(page, LayoutTokens.Space8), Scale(page, LayoutTokens.Space8));
        page.Padding = EnsureMinimumPadding(page, page.Padding, LayoutTokens.Space8);
    }

    private static void HardenSplit(SplitContainer split)
    {
        split.IsSplitterFixed = false;
        split.SplitterWidth = Math.Max(split.SplitterWidth, Scale(split, 6));
        ApplySplitBounds(split);
    }

    private static void ApplySplitBounds(SplitContainer split)
    {
        if (split.Panel1Collapsed || split.Panel2Collapsed) return;

        var available = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        var paneMinimum = Scale(split, LayoutTokens.SplitPaneMinimum);
        if (available <= (paneMinimum * 2) + split.SplitterWidth) return;

        split.Panel1MinSize = Math.Max(split.Panel1MinSize, paneMinimum);
        split.Panel2MinSize = Math.Max(split.Panel2MinSize, paneMinimum);

        var minDistance = split.Panel1MinSize;
        var maxDistance = Math.Max(minDistance, available - split.SplitterWidth - split.Panel2MinSize);
        split.SplitterDistance = Math.Clamp(split.SplitterDistance, minDistance, maxDistance);
    }

    private static void HardenGrid(DataGridView grid)
    {
        grid.ShowCellToolTips = true;
        grid.AllowUserToResizeColumns = true;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.MinimumWidth = Math.Max(column.MinimumWidth, Scale(grid, 48));
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        }
    }

    private static void HardenRichText(RichTextBox rich)
    {
        if (!rich.ReadOnly) return;

        var looksLikeCodeOrLog = rich.Font.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                                 || rich.Font.Name.Contains("Cascadia", StringComparison.OrdinalIgnoreCase)
                                 || ContainsIgnoreCase(rich.AccessibleName, "code")
                                 || ContainsIgnoreCase(rich.AccessibleName, "log")
                                 || ContainsIgnoreCase(rich.AccessibleName, "activity");

        if (looksLikeCodeOrLog)
        {
            rich.WordWrap = false;
            rich.ScrollBars = RichTextBoxScrollBars.Both;
        }
        else
        {
            rich.WordWrap = true;
            rich.ScrollBars = RichTextBoxScrollBars.Vertical;
        }
    }

    private static void HookLongTextTooltip(Control control, FormRegistration registration)
    {
        var state = Controls.GetValue(control, _ => new ControlRegistration());
        if (!state.TextHooked)
        {
            state.TextHooked = true;
            control.TextChanged += (_, _) => UpdateTooltip(control, registration);
            control.SizeChanged += (_, _) => UpdateTooltip(control, registration);
        }

        UpdateTooltip(control, registration);
    }

    private static void UpdateTooltip(Control control, FormRegistration registration)
    {
        if (registration.ToolTip is null || control.IsDisposed) return;

        var text = control.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            registration.ToolTip.SetToolTip(control, null);
            return;
        }

        var measured = TextRenderer.MeasureText(
            text,
            control.Font,
            new Size(int.MaxValue, Math.Max(1, control.Height)),
            TextFormatFlags.SingleLine);
        var available = Math.Max(1, control.ClientSize.Width - control.Padding.Horizontal - Scale(control, LayoutTokens.Space8));
        var needsTooltip = text.Length >= 48 || measured.Width > available;
        registration.ToolTip.SetToolTip(control, needsTooltip ? text : null);
    }

    private static void ApplyResponsiveState(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        // Main/Settings forms already have a dedicated responsive owner in
        // SecondaryScreenExperience. Keep generic hardening active, but do not let
        // this fallback layer competitively rewrite their resize geometry.
        if (HasSpecializedResponsiveOwner(form)) return;

        var compact = form.ClientSize.Width < Scale(form, LayoutTokens.CompactBreakpoint);
        var narrow = form.ClientSize.Width < Scale(form, LayoutTokens.NarrowBreakpoint);

        foreach (Control control in Descendants(form))
        {
            switch (control)
            {
                case FlowLayoutPanel flow when flow.Controls.OfType<Button>().Any() && !IsSingleRowCommandFlow(flow):
                    flow.WrapContents = compact;
                    flow.Padding = EnsureMinimumPadding(flow, flow.Padding, compact ? LayoutTokens.Space4 : LayoutTokens.Space8);
                    break;

                case TabControl tabs:
                    tabs.Padding = compact
                        ? new Point(Scale(tabs, 10), Scale(tabs, 6))
                        : new Point(Scale(tabs, 16), Scale(tabs, 7));
                    break;

                case SplitContainer split:
                    ApplySplitBounds(split);
                    break;

                case Button button when narrow:
                    button.MinimumSize = new Size(
                        button.MinimumSize.Width,
                        Math.Max(button.MinimumSize.Height, Scale(button, LayoutTokens.CompactControlHeight)));
                    break;
            }
        }
    }

    private static bool HasSpecializedResponsiveOwner(Form form)
        => form is MainForm or SettingsForm or MonitorSettingsForm;

    private static bool IsSingleRowCommandFlow(FlowLayoutPanel flow)
    {
        var labels = flow.Controls.OfType<Button>()
            .Select(button => Normalize(button.Text))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Development Plan has a deliberately sized command strip from UI-POLISH-006.
        return labels.Contains("Start") && labels.Contains("Schedule");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static Padding EnsureMinimumPadding(Control control, Padding current, int logicalMinimum)
    {
        var minimum = Scale(control, logicalMinimum);
        return new Padding(
            Math.Max(current.Left, minimum),
            Math.Max(current.Top, minimum),
            Math.Max(current.Right, minimum),
            Math.Max(current.Bottom, minimum));
    }

    private static Padding Scale(Control control, Padding logical)
        => new(
            Scale(control, logical.Left),
            Scale(control, logical.Top),
            Scale(control, logical.Right),
            Scale(control, logical.Bottom));

    private static int Scale(Control control, int logical)
        => Math.Max(0, (int)Math.Round(logical * Math.Max(96, control.DeviceDpi) / 96d));

    private static string Normalize(string? text)
        => (text ?? string.Empty).Replace("&", string.Empty).Replace("…", string.Empty).Trim();

    private static bool ContainsIgnoreCase(string? value, string fragment)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private sealed class FormRegistration
    {
        public bool Initialized { get; set; }
        public ToolTip? ToolTip { get; set; }
    }

    private sealed class ControlRegistration
    {
        public bool ChildHooked { get; set; }
        public bool TextHooked { get; set; }
    }
}
