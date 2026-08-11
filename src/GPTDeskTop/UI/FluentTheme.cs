using System.Drawing.Drawing2D;

namespace GPTDeskTop.UI;

public static class FluentTheme
{
    public static readonly Color Background = Color.FromArgb(245, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color SurfaceHover = Color.FromArgb(241, 245, 249);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color AccentSubtle = Color.FromArgb(239, 246, 255);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color Danger = Color.FromArgb(190, 24, 93);
    public static readonly Color DangerSubtle = Color.FromArgb(253, 242, 248);
    public static readonly Color Success = Color.FromArgb(5, 150, 105);
    public static readonly Color SuccessSubtle = Color.FromArgb(236, 253, 245);
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);
    public static readonly Color WarningSubtle = Color.FromArgb(255, 251, 235);

    private static readonly Font BodyFont = new("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font ButtonFont = new("Segoe UI Variable Text", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font SectionFont = new("Segoe UI Variable Display", 11F, FontStyle.Bold, GraphicsUnit.Point);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = BodyFont;
        ApplyRecursive(form.Controls);
    }

    private static void ApplyRecursive(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case GroupBox group:
                    group.ForeColor = Text;
                    group.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
                    if (group.BackColor == SystemColors.Control) group.BackColor = Background;
                    break;
                case TextBox box:
                    StyleTextBox(box);
                    break;
                case RichTextBox rich:
                    rich.BorderStyle = BorderStyle.None;
                    rich.BackColor = Surface;
                    rich.ForeColor = Text;
                    break;
                case NumericUpDown numeric:
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Text;
                    numeric.Font = BodyFont;
                    break;
                case ComboBox combo:
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.Font = BodyFont;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    check.Font = BodyFont;
                    break;
                case Label label:
                    if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
                    break;
                case TabControl tabs:
                    tabs.Font = BodyFont;
                    tabs.Padding = new Point(16, 7);
                    tabs.SizeMode = TabSizeMode.Fixed;
                    tabs.ItemSize = new Size(Math.Max(110, tabs.ItemSize.Width), 34);
                    break;
                case TabPage page:
                    page.BackColor = Surface;
                    page.ForeColor = Text;
                    page.Padding = new Padding(8);
                    break;
                case SplitContainer split:
                    split.BackColor = Background;
                    break;
                case TableLayoutPanel table:
                    if (table.BackColor == SystemColors.Control) table.BackColor = Background;
                    break;
                case FlowLayoutPanel flow:
                    if (flow.BackColor == SystemColors.Control) flow.BackColor = Background;
                    break;
                case Panel panel:
                    if (panel.BorderStyle == BorderStyle.FixedSingle)
                        StyleCard(panel);
                    else if (panel.BackColor == SystemColors.Control)
                        panel.BackColor = Background;
                    break;
            }

            if (control.HasChildren) ApplyRecursive(control.Controls);
        }
    }

    public static void StyleButton(Button button, bool primary = false, bool danger = false)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : danger ? DangerSubtle : SurfaceHover;
        button.FlatAppearance.MouseDownBackColor = primary ? AccentPressed : danger ? Color.FromArgb(252, 231, 243) : AccentSubtle;
        button.Padding = new Padding(14, 6, 14, 6);
        button.Margin = new Padding(4);
        button.MinimumSize = new Size(0, 36);
        button.Cursor = Cursors.Hand;
        button.Font = ButtonFont;
        button.TextAlign = ContentAlignment.MiddleCenter;

        if (danger)
        {
            button.BackColor = Surface;
            button.ForeColor = Danger;
            button.FlatAppearance.BorderColor = Color.FromArgb(249, 168, 212);
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

        ApplyRoundedRegion(button, 8);
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
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Variable Text", 8.75F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = AccentSubtle;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(7, 4, 7, 4);
        grid.DefaultCellStyle.Font = BodyFont;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 254);
        grid.RowTemplate.Height = 38;
        grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
    }

    public static Label CreateSectionTitle(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = SectionFont,
            ForeColor = Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

    public static Label CreateMutedLabel(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font("Segoe UI Variable Text", 8.75F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

    public static ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new FluentMenuColorTable()),
            Font = new Font("Segoe UI Variable Text", 9F),
            BackColor = Surface,
            ForeColor = Text,
            ShowImageMargin = true,
            Padding = new Padding(4)
        };
        return menu;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = box.ReadOnly ? SurfaceAlt : Surface;
        box.ForeColor = box.ReadOnly ? Muted : Text;
        box.Font = BodyFont;
    }

    private static void StyleCard(Panel panel)
    {
        panel.BorderStyle = BorderStyle.None;
        panel.BackColor = Surface;
        panel.Padding = EnsureMinimumPadding(panel.Padding, 1);
        ApplyRoundedRegion(panel, 10);
        panel.Paint += (_, e) =>
        {
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, Math.Max(1, panel.Width - 1), Math.Max(1, panel.Height - 1)), 10);
            using var pen = new Pen(Border);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        };
    }

    private static Padding EnsureMinimumPadding(Padding padding, int minimum)
        => new(
            Math.Max(minimum, padding.Left),
            Math.Max(minimum, padding.Top),
            Math.Max(minimum, padding.Right),
            Math.Max(minimum, padding.Bottom));

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        void UpdateRegion()
        {
            if (control.Width <= 1 || control.Height <= 1) return;
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, control.Width, control.Height), radius);
            control.Region?.Dispose();
            control.Region = new Region(path);
        }

        UpdateRegion();
        control.Resize += (_, _) => UpdateRegion();
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
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

    private sealed class FluentMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => AccentSubtle;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => BorderStrong;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }
}
