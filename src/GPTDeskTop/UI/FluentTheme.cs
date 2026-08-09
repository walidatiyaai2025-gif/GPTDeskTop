namespace GPTDeskTop.UI;

public static class FluentTheme
{
    public static readonly Color Background = Color.FromArgb(243, 246, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color AccentHover = Color.FromArgb(16, 110, 190);
    public static readonly Color AccentSubtle = Color.FromArgb(232, 242, 252);
    public static readonly Color Text = Color.FromArgb(32, 31, 30);
    public static readonly Color Muted = Color.FromArgb(96, 94, 92);
    public static readonly Color Border = Color.FromArgb(225, 230, 235);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);
    public static readonly Color DangerSubtle = Color.FromArgb(253, 238, 236);
    public static readonly Color Success = Color.FromArgb(16, 124, 65);
    public static readonly Color SuccessSubtle = Color.FromArgb(232, 247, 239);
    public static readonly Color Warning = Color.FromArgb(157, 93, 0);
    public static readonly Color WarningSubtle = Color.FromArgb(255, 246, 220);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
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
                    if (group.BackColor == SystemColors.Control) group.BackColor = Background;
                    break;
                case TextBox box:
                    box.BorderStyle = BorderStyle.FixedSingle;
                    box.BackColor = Surface;
                    box.ForeColor = Text;
                    break;
                case RichTextBox rich:
                    rich.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown numeric:
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Text;
                    break;
                case ComboBox combo:
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case Label label:
                    if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
                    break;
                case TabControl tabs:
                    tabs.Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular);
                    tabs.Padding = new Point(16, 6);
                    break;
                case TabPage page:
                    page.BackColor = Surface;
                    page.ForeColor = Text;
                    break;
                case TableLayoutPanel table:
                    if (table.BackColor == SystemColors.Control) table.BackColor = Background;
                    break;
                case FlowLayoutPanel flow:
                    if (flow.BackColor == SystemColors.Control) flow.BackColor = Background;
                    break;
                case Panel panel:
                    if (panel.BackColor == SystemColors.Control) panel.BackColor = Background;
                    break;
            }
            if (control.HasChildren) ApplyRecursive(control.Controls);
        }
    }

    public static void StyleButton(Button button, bool primary = false, bool danger = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : danger ? DangerSubtle : AccentSubtle;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 232, 242);
        button.Padding = new Padding(12, 5, 12, 5);
        button.Margin = new Padding(4);
        button.MinimumSize = new Size(0, 34);
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular);
        if (danger)
        {
            button.BackColor = Surface;
            button.ForeColor = Danger;
            button.FlatAppearance.BorderColor = Color.FromArgb(235, 170, 165);
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
            button.FlatAppearance.BorderColor = Border;
        }
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = AccentSubtle;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
    }

    public static Label CreateSectionTitle(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 11F, FontStyle.Bold),
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
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

    public static ContextMenuStrip CreateMenu()
        => new() { Renderer = new ToolStripProfessionalRenderer(), Font = new Font("Segoe UI Variable Text", 9F) };
}
