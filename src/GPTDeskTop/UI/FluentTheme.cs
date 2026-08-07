namespace GPTDeskTop.UI;

public static class FluentTheme
{
    public static readonly Color Background = Color.FromArgb(243, 246, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color AccentHover = Color.FromArgb(16, 110, 190);
    public static readonly Color Text = Color.FromArgb(32, 31, 30);
    public static readonly Color Muted = Color.FromArgb(96, 94, 92);
    public static readonly Color Border = Color.FromArgb(225, 223, 221);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);

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
                    group.BackColor = Background;
                    break;
                case TextBox box:
                    box.BorderStyle = BorderStyle.FixedSingle;
                    box.BackColor = Surface;
                    box.ForeColor = Text;
                    break;
                case RichTextBox rich:
                    rich.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case Label label:
                    if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
                    break;
                case Panel panel:
                    panel.BackColor = Background;
                    break;
                case TableLayoutPanel table:
                    table.BackColor = Background;
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = Background;
                    break;
            }
            if (control.HasChildren) ApplyRecursive(control.Controls);
        }
    }

    public static void StyleButton(Button button, bool primary = false, bool danger = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(235, 242, 248);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 232, 242);
        button.Padding = new Padding(12, 5, 12, 5);
        button.Margin = new Padding(4);
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
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 236, 249);
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersHeight = 34;
    }

    public static ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(), Font = new Font("Segoe UI Variable Text", 9F) };
        return menu;
    }
}
