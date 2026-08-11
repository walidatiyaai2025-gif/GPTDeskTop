using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class LayoutStabilityBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => LayoutStability.ApplyOpenForms();
}

/// <summary>
/// Reusable WinForms layout hardening. The layer is intentionally presentation-only:
/// it never reads or mutates monitor/runtime/persistence state.
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
        if (!registration.Initialized)
        {
            registration.Initialized = true;
            registration.ToolTip = new ToolTip
            {
                AutoPopDelay = 10000,
                InitialDelay = 450,
                ReshowDelay = 120,
                ShowAlways = true
            };

            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.MinimumSize = new Size(
                Math.Max(form.MinimumSize.Width, LayoutTokens.MinimumUsableWidth),
                Math.Max(form.MinimumSize.Height, LayoutTokens.MinimumUsableHeight));

            form.Resize += (_, _) => ApplyResponsiveState(form);
            form.FormClosed += (_, _) =>
            {
                registration.ToolTip?.Dispose();
                registration.ToolTip = null;
            };
        }

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
        button.AutoEllipsis = true;
        button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(button.MinimumSize.Height, LayoutTokens.ControlHeight));
        if (button.Margin == Padding.Empty)
            button.Margin = LayoutTokens.ControlMargin;
        HookLongTextTooltip(button, registration);
    }

    private static void HardenTextBox(TextBox box)
    {
        if (!box.Multiline)
            box.MinimumSize = new Size(box.MinimumSize.Width, Math.Max(box.MinimumSize.Height, LayoutTokens.CompactControlHeight));

        if (box.Multiline && box.ScrollBars == ScrollBars.None && (box.ReadOnly || box.AcceptsReturn))
            box.ScrollBars = ScrollBars.Vertical;
    }

    private static void HardenInput(Control control)
    {
        control.MinimumSize = new Size(control.MinimumSize.Width, Math.Max(control.MinimumSize.Height, LayoutTokens.CompactControlHeight));
    }

    private static void HardenFlow(FlowLayoutPanel flow)
    {
        if (!flow.Controls.OfType<Button>().Any()) return;
        flow.WrapContents = true;
        flow.FlowDirection = FlowDirection.LeftToRight;
        flow.AutoScroll = false;
        if (flow.Margin == Padding.Empty)
            flow.Margin = LayoutTokens.ControlMargin;
    }

    private static void HardenTable(TableLayoutPanel table)
    {
        table.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        if (table.Margin == Padding.Empty)
            table.Margin = LayoutTokens.ControlMargin;

        foreach (Control child in table.Controls)
        {
            if (child.Dock == DockStyle.Fill && child.Margin == Padding.Empty)
                child.Margin = LayoutTokens.ControlMargin;
        }
    }

    private static void HardenPage(TabPage page)
    {
        page.AutoScroll = true;
        page.AutoScrollMargin = new Size(LayoutTokens.Space8, LayoutTokens.Space8);
        page.Padding = EnsureMinimumPadding(page.Padding, LayoutTokens.Space8);
    }

    private static void HardenSplit(SplitContainer split)
    {
        split.IsSplitterFixed = false;
        split.SplitterWidth = Math.Max(split.SplitterWidth, 6);
        ApplySplitBounds(split);
    }

    private static void ApplySplitBounds(SplitContainer split)
    {
        var available = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        if (available <= (LayoutTokens.SplitPaneMinimum * 2) + split.SplitterWidth) return;

        split.Panel1MinSize = Math.Max(split.Panel1MinSize, LayoutTokens.SplitPaneMinimum);
        split.Panel2MinSize = Math.Max(split.Panel2MinSize, LayoutTokens.SplitPaneMinimum);

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
            column.MinimumWidth = Math.Max(column.MinimumWidth, 48);
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

        var measured = TextRenderer.MeasureText(text, control.Font, new Size(int.MaxValue, Math.Max(1, control.Height)), TextFormatFlags.SingleLine);
        var available = Math.Max(1, control.ClientSize.Width - control.Padding.Horizontal - LayoutTokens.Space8);
        var needsTooltip = text.Length >= 48 || measured.Width > available;
        registration.ToolTip.SetToolTip(control, needsTooltip ? text : null);
    }

    private static void ApplyResponsiveState(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var compact = form.ClientSize.Width < LayoutTokens.CompactBreakpoint;
        var narrow = form.ClientSize.Width < LayoutTokens.NarrowBreakpoint;

        foreach (Control control in Descendants(form))
        {
            switch (control)
            {
                case FlowLayoutPanel flow when flow.Controls.OfType<Button>().Any():
                    flow.WrapContents = true;
                    flow.Padding = EnsureMinimumPadding(flow.Padding, compact ? LayoutTokens.Space4 : LayoutTokens.Space8);
                    break;

                case TabControl tabs:
                    tabs.Padding = compact ? new Point(10, 6) : new Point(16, 7);
                    break;

                case SplitContainer split:
                    ApplySplitBounds(split);
                    break;

                case Button button when narrow:
                    button.MinimumSize = new Size(button.MinimumSize.Width, LayoutTokens.CompactControlHeight);
                    break;
            }
        }
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

    private static Padding EnsureMinimumPadding(Padding current, int minimum)
        => new(
            Math.Max(current.Left, minimum),
            Math.Max(current.Top, minimum),
            Math.Max(current.Right, minimum),
            Math.Max(current.Bottom, minimum));

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
