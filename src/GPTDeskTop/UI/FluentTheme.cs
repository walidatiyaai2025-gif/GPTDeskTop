using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

public static class FluentTheme
{
    // Premium dark runtime palette. The colors deliberately preserve the existing semantic
    // roles so every current screen and runtime state keeps the same behavior and meaning.
    public static readonly Color Background = Color.FromArgb(5, 14, 24);
    public static readonly Color Surface = Color.FromArgb(9, 23, 38);
    public static readonly Color SurfaceAlt = Color.FromArgb(12, 29, 47);
    public static readonly Color SurfaceRaised = Color.FromArgb(7, 20, 34);
    public static readonly Color SurfaceHover = Color.FromArgb(16, 40, 65);
    public static readonly Color SurfacePressed = Color.FromArgb(22, 53, 84);
    public static readonly Color Accent = Color.FromArgb(10, 113, 255);
    public static readonly Color AccentHover = Color.FromArgb(39, 130, 255);
    public static readonly Color AccentPressed = Color.FromArgb(0, 91, 214);
    public static readonly Color AccentSubtle = Color.FromArgb(11, 42, 74);
    public static readonly Color AccentBorder = Color.FromArgb(29, 104, 192);
    public static readonly Color Text = Color.FromArgb(235, 243, 255);
    public static readonly Color Muted = Color.FromArgb(135, 153, 179);
    public static readonly Color MutedStrong = Color.FromArgb(177, 194, 215);
    public static readonly Color DisabledText = Color.FromArgb(89, 108, 132);
    public static readonly Color DisabledSurface = Color.FromArgb(17, 31, 47);
    public static readonly Color Border = Color.FromArgb(28, 48, 70);
    public static readonly Color BorderStrong = Color.FromArgb(42, 67, 96);
    public static readonly Color FocusRing = Color.FromArgb(66, 153, 255);
    public static readonly Color Danger = Color.FromArgb(248, 81, 96);
    public static readonly Color DangerSubtle = Color.FromArgb(63, 25, 34);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color SuccessSubtle = Color.FromArgb(12, 52, 43);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color WarningSubtle = Color.FromArgb(60, 43, 15);
    public static readonly Color Info = Color.FromArgb(56, 189, 248);
    public static readonly Color InfoSubtle = Color.FromArgb(12, 44, 62);

    private static readonly Font BodyFont = new("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font BodyStrongFont = new("Segoe UI Variable Text", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font ButtonFont = new("Segoe UI Variable Text", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font CaptionFont = new("Segoe UI Variable Text", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font CaptionStrongFont = new("Segoe UI Variable Text", 8.25F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font SectionFont = new("Segoe UI Variable Display", 11F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font GridHeaderFont = new("Segoe UI Variable Text", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly ConditionalWeakTable<Control, ThemeRegistration> Registrations = new();

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = BodyFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        ApplyAccessibilityDefaults(form);
        ApplyRecursive(form.Controls);
    }

    private static void ApplyRecursive(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            ApplyAccessibilityDefaults(control);

            switch (control)
            {
                case Button button: StyleButton(button); break;
                case DataGridView grid: StyleGrid(grid); break;
                case GroupBox group: StyleGroupBox(group); break;
                case TextBox box: StyleTextBox(box); break;
                case RichTextBox rich: StyleRichTextBox(rich); break;
                case NumericUpDown numeric: StyleNumeric(numeric); break;
                case ComboBox combo: StyleCombo(combo); break;
                case CheckBox check: StyleCheckBox(check); break;
                case RadioButton radio: StyleRadioButton(radio); break;
                case LinkLabel link: StyleLinkLabel(link); break;
                case Label label: StyleLabel(label); break;
                case TabControl tabs: StyleTabs(tabs); break;
                case TabPage page: StyleTabPage(page); break;
                case SplitContainer split: StyleSplitContainer(split); break;
                case TableLayoutPanel table: StyleLayoutPanel(table); break;
                case FlowLayoutPanel flow: StyleLayoutPanel(flow); break;
                case Panel panel:
                    if (panel.BorderStyle == BorderStyle.FixedSingle) StyleCard(panel);
                    else if (panel.BackColor == SystemColors.Control) panel.BackColor = Background;
                    break;
                case CheckedListBox checkedList: StyleListBox(checkedList); break;
                case ListBox list: StyleListBox(list); break;
                case ListView listView: StyleListView(listView); break;
                case TreeView tree: StyleTreeView(tree); break;
                case DateTimePicker picker: StyleDateTimePicker(picker); break;
                case ProgressBar progress: StyleProgressBar(progress); break;
                case ToolStrip toolStrip: StyleToolStrip(toolStrip); break;
            }

            if (control.HasChildren) ApplyRecursive(control.Controls);
        }
    }

    public static void StyleButton(Button button, bool primary = false, bool danger = false)
    {
        var registration = GetRegistration(button);
        registration.ButtonPrimary = primary;
        registration.ButtonDanger = danger;

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : danger ? DangerSubtle : SurfaceHover;
        button.FlatAppearance.MouseDownBackColor = primary ? AccentPressed : danger ? Color.FromArgb(82, 31, 42) : SurfacePressed;
        button.Padding = new Padding(14, 6, 14, 6);
        button.Margin = new Padding(4);
        button.MinimumSize = new Size(0, 36);
        button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
        button.Font = ButtonFont;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.AutoEllipsis = true;
        ApplyButtonColors(button, primary, danger);
        ApplyRoundedRegion(button, 8);

        if (registration.ButtonEvents) return;
        registration.ButtonEvents = true;
        button.EnabledChanged += (_, _) =>
        {
            var state = GetRegistration(button);
            ApplyButtonColors(button, state.ButtonPrimary, state.ButtonDanger);
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
            button.Invalidate();
        };
        button.GotFocus += (_, _) => button.Invalidate();
        button.LostFocus += (_, _) => button.Invalidate();
        button.Paint += (_, e) => DrawButtonFocusRing(button, e.Graphics);
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersVisible = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = MutedStrong;
        grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(7, 4, 7, 4);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = AccentSubtle;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        grid.DefaultCellStyle.Font = BodyFont;
        grid.DefaultCellStyle.NullValue = "—";
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(11, 27, 44);
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = AccentSubtle;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Text;
        grid.RowTemplate.Height = 38;
        grid.RowTemplate.Resizable = DataGridViewTriState.False;
        grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ShowCellErrors = false;
        grid.ShowRowErrors = false;
    }

    public static Label CreateSectionTitle(string text)
        => new() { Text = text, Dock = DockStyle.Fill, Font = SectionFont, ForeColor = Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };

    public static Label CreateMutedLabel(string text)
        => new() { Text = text, Dock = DockStyle.Fill, ForeColor = Muted, Font = CaptionFont, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };

    public static Label CreateEyebrowLabel(string text)
        => new() { Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = MutedStrong, Font = CaptionStrongFont, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

    public static Panel CreateDivider()
        => new() { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 6, 0, 6) };

    public static ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new FluentMenuColorTable()),
            Font = BodyFont,
            BackColor = Surface,
            ForeColor = Text,
            ShowImageMargin = true,
            Padding = new Padding(4),
            ShowCheckMargin = false
        };

        var registration = GetRegistration(menu);
        if (registration.MenuEvents) return menu;
        registration.MenuEvents = true;
        menu.Opening += (_, _) =>
        {
            foreach (ToolStripItem item in menu.Items)
            {
                item.Padding = item is ToolStripSeparator ? Padding.Empty : new Padding(8, 4, 8, 4);
                item.Margin = item is ToolStripSeparator ? new Padding(2, 4, 2, 4) : new Padding(0, 1, 0, 1);
            }
        };
        return menu;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = BodyFont;
        box.ShortcutsEnabled = true;
        ApplyInputColors(box, box.ReadOnly);
        RegisterInputFocus(box, box.ReadOnly);
    }

    private static void StyleRichTextBox(RichTextBox rich)
    {
        rich.BorderStyle = BorderStyle.None;
        rich.BackColor = rich.ReadOnly ? SurfaceAlt : Surface;
        rich.ForeColor = rich.ReadOnly ? MutedStrong : Text;
        rich.Font = BodyFont;
        rich.ShortcutsEnabled = true;
    }

    private static void StyleNumeric(NumericUpDown numeric)
    {
        numeric.BorderStyle = BorderStyle.FixedSingle;
        numeric.BackColor = Surface;
        numeric.ForeColor = Text;
        numeric.Font = BodyFont;
        numeric.TextAlign = HorizontalAlignment.Left;
        RegisterInputFocus(numeric, readOnly: false);
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = Surface;
        combo.ForeColor = Text;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = BodyFont;
        combo.IntegralHeight = false;
        combo.DropDownHeight = Math.Max(combo.DropDownHeight, 240);
        RegisterInputFocus(combo, readOnly: combo.DropDownStyle == ComboBoxStyle.DropDownList);
    }

    private static void StyleCheckBox(CheckBox check)
    {
        check.ForeColor = check.Enabled ? Text : DisabledText;
        check.Font = BodyFont;
        check.FlatStyle = FlatStyle.System;
        check.UseVisualStyleBackColor = true;
    }

    private static void StyleRadioButton(RadioButton radio)
    {
        radio.ForeColor = radio.Enabled ? Text : DisabledText;
        radio.Font = BodyFont;
        radio.FlatStyle = FlatStyle.System;
        radio.UseVisualStyleBackColor = true;
    }

    private static void StyleLabel(Label label)
    {
        if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
    }

    private static void StyleLinkLabel(LinkLabel link)
    {
        link.LinkColor = Accent;
        link.ActiveLinkColor = AccentPressed;
        link.VisitedLinkColor = AccentHover;
        link.DisabledLinkColor = DisabledText;
        link.Font = BodyStrongFont;
        link.LinkBehavior = LinkBehavior.HoverUnderline;
    }

    private static void StyleTabs(TabControl tabs)
    {
        tabs.Font = BodyFont;
        tabs.Padding = new Point(16, 7);
        tabs.ItemSize = new Size(Math.Max(80, tabs.ItemSize.Width), Math.Max(32, tabs.ItemSize.Height));
        tabs.SizeMode = TabSizeMode.Normal;
    }

    private static void StyleTabPage(TabPage page)
    {
        page.BackColor = Surface;
        page.ForeColor = Text;
        page.Padding = EnsureMinimumPadding(page.Padding, 8);
    }

    private static void StyleSplitContainer(SplitContainer split)
    {
        split.BackColor = Background;
        split.SplitterWidth = Math.Max(split.SplitterWidth, 6);
        split.TabStop = false;
    }

    private static void StyleLayoutPanel(Control panel)
    {
        if (panel.BackColor == SystemColors.Control) panel.BackColor = Background;
    }

    private static void StyleGroupBox(GroupBox group)
    {
        group.ForeColor = Text;
        group.Font = BodyStrongFont;
        group.Padding = EnsureMinimumPadding(group.Padding, 10);
        if (group.BackColor == SystemColors.Control) group.BackColor = Background;
    }

    private static void StyleListBox(ListBox list)
    {
        list.BackColor = Surface;
        list.ForeColor = Text;
        list.Font = BodyFont;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.IntegralHeight = false;
    }

    private static void StyleListView(ListView list)
    {
        list.BackColor = Surface;
        list.ForeColor = Text;
        list.Font = BodyFont;
        list.BorderStyle = BorderStyle.None;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.GridLines = false;
    }

    private static void StyleTreeView(TreeView tree)
    {
        tree.BackColor = Surface;
        tree.ForeColor = Text;
        tree.Font = BodyFont;
        tree.BorderStyle = BorderStyle.None;
        tree.HideSelection = false;
        tree.HotTracking = true;
        tree.ShowNodeToolTips = true;
        tree.ItemHeight = Math.Max(tree.ItemHeight, 24);
    }

    private static void StyleDateTimePicker(DateTimePicker picker)
    {
        picker.BackColor = Surface;
        picker.ForeColor = Text;
        picker.Font = BodyFont;
        picker.CalendarForeColor = Text;
        picker.CalendarMonthBackground = Surface;
        picker.CalendarTitleBackColor = Accent;
        picker.CalendarTitleForeColor = Color.White;
        picker.CalendarTrailingForeColor = Muted;
    }

    private static void StyleProgressBar(ProgressBar progress)
    {
        progress.ForeColor = Accent;
        progress.BackColor = SurfaceAlt;
        if (progress.Style != ProgressBarStyle.Marquee) progress.Style = ProgressBarStyle.Continuous;
    }

    private static void StyleToolStrip(ToolStrip toolStrip)
    {
        toolStrip.Renderer = new ToolStripProfessionalRenderer(new FluentMenuColorTable());
        toolStrip.BackColor = Surface;
        toolStrip.ForeColor = Text;
        toolStrip.Font = BodyFont;
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.Padding = new Padding(8, 5, 8, 5);
    }

    private static void StyleCard(Panel panel)
    {
        panel.BorderStyle = BorderStyle.None;
        panel.BackColor = Surface;
        panel.Padding = EnsureMinimumPadding(panel.Padding, 1);
        ApplyRoundedRegion(panel, 10);

        var registration = GetRegistration(panel);
        if (registration.CardPaint) return;
        registration.CardPaint = true;
        panel.Paint += (_, e) =>
        {
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, Math.Max(1, panel.Width - 1), Math.Max(1, panel.Height - 1)), 10);
            using var pen = new Pen(Border);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        };
    }

    private static void ApplyButtonColors(Button button, bool primary, bool danger)
    {
        if (!button.Enabled)
        {
            button.BackColor = DisabledSurface;
            button.ForeColor = DisabledText;
            button.FlatAppearance.BorderColor = Border;
            return;
        }

        if (danger)
        {
            button.BackColor = Surface;
            button.ForeColor = Danger;
            button.FlatAppearance.BorderColor = Color.FromArgb(121, 50, 62);
        }
        else if (primary)
        {
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Accent;
        }
        else
        {
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = BorderStrong;
        }
    }

    private static void DrawButtonFocusRing(Button button, Graphics graphics)
    {
        if (!button.Focused || !button.Enabled) return;
        var bounds = new Rectangle(2, 2, Math.Max(1, button.Width - 5), Math.Max(1, button.Height - 5));
        using var path = CreateRoundedRectangle(bounds, 6);
        using var pen = new Pen(FocusRing, 1.5F);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawPath(pen, path);
    }

    private static void ApplyInputColors(Control control, bool readOnly)
    {
        control.BackColor = !control.Enabled ? DisabledSurface : readOnly ? SurfaceAlt : control.Focused ? AccentSubtle : Surface;
        control.ForeColor = !control.Enabled ? DisabledText : readOnly ? MutedStrong : Text;
    }

    private static void RegisterInputFocus(Control control, bool readOnly)
    {
        var registration = GetRegistration(control);
        registration.InputReadOnly = readOnly;
        if (registration.InputEvents) return;

        registration.InputEvents = true;
        control.Enter += (_, _) => ApplyInputColors(control, GetRegistration(control).InputReadOnly);
        control.Leave += (_, _) => ApplyInputColors(control, GetRegistration(control).InputReadOnly);
        control.EnabledChanged += (_, _) => ApplyInputColors(control, GetRegistration(control).InputReadOnly);
    }

    private static void ApplyAccessibilityDefaults(Control control)
    {
        if (string.IsNullOrWhiteSpace(control.AccessibleName) && !string.IsNullOrWhiteSpace(control.Text))
            control.AccessibleName = control.Text.Replace("&", string.Empty).Trim();

        if (control is Button or TextBox or ComboBox or NumericUpDown or CheckBox or RadioButton or ListBox or ListView or TreeView)
            control.AccessibleDescription ??= control.AccessibleName;
    }

    private static Padding EnsureMinimumPadding(Padding padding, int minimum)
        => new(Math.Max(minimum, padding.Left), Math.Max(minimum, padding.Top), Math.Max(minimum, padding.Right), Math.Max(minimum, padding.Bottom));

    private static ThemeRegistration GetRegistration(Control control)
        => Registrations.GetValue(control, _ => new ThemeRegistration());

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        var registration = GetRegistration(control);
        registration.Radius = Math.Max(registration.Radius, radius);

        void UpdateRegion()
        {
            if (control.Width <= 1 || control.Height <= 1) return;
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, control.Width, control.Height), GetRegistration(control).Radius);
            var oldRegion = control.Region;
            control.Region = new Region(path);
            oldRegion?.Dispose();
        }

        UpdateRegion();
        if (registration.RoundedRegion) return;
        registration.RoundedRegion = true;
        control.Resize += (_, _) => UpdateRegion();
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var effectiveRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        var diameter = Math.Max(2, effectiveRadius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ThemeRegistration
    {
        public bool RoundedRegion { get; set; }
        public bool CardPaint { get; set; }
        public bool ButtonEvents { get; set; }
        public bool ButtonPrimary { get; set; }
        public bool ButtonDanger { get; set; }
        public bool InputEvents { get; set; }
        public bool InputReadOnly { get; set; }
        public bool MenuEvents { get; set; }
        public int Radius { get; set; }
    }

    private sealed class FluentMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => AccentSubtle;
        public override Color MenuItemSelectedGradientBegin => AccentSubtle;
        public override Color MenuItemSelectedGradientEnd => AccentSubtle;
        public override Color MenuItemBorder => AccentBorder;
        public override Color MenuBorder => BorderStrong;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color ButtonSelectedBorder => AccentBorder;
        public override Color ButtonSelectedHighlight => AccentSubtle;
        public override Color ButtonPressedHighlight => SurfacePressed;
    }
}