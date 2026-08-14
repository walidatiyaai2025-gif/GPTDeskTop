namespace GPTDeskTop.UI;

public sealed class ProjectInitializeDialog : Form
{
    private readonly TextBox _repository = new() { Dock = DockStyle.Fill, PlaceholderText = "https://github.com/owner/repository" };
    private readonly TextBox _branch = new() { Dock = DockStyle.Fill, Text = "main" };
    private readonly TextBox _goal = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _ok = new() { Text = "Initialize Project", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public string RepositoryUrl => _repository.Text.Trim();
    public string Branch => _branch.Text.Trim();
    public string MainGoal => _goal.Text.Trim();

    public ProjectInitializeDialog()
    {
        Text = "Initialize GitHub Project";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 390);
        Size = new Size(700, 430);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(18), BackColor = FluentTheme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = FluentTheme.CreateSectionTitle("Initialize Project Automation");
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        root.Controls.Add(MakeLabel("Repository URL"), 0, 1); root.Controls.Add(_repository, 1, 1);
        root.Controls.Add(MakeLabel("Branch"), 0, 2); root.Controls.Add(_branch, 1, 2);
        root.Controls.Add(MakeLabel("Main goal"), 0, 3); root.Controls.Add(_goal, 1, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.Add(_ok); actions.Controls.Add(_cancel);
        root.Controls.Add(actions, 0, 4); root.SetColumnSpan(actions, 2);
        Controls.Add(root);

        AcceptButton = _ok;
        CancelButton = _cancel;
        FluentTheme.StyleButton(_ok, primary: true);
        FluentTheme.StyleButton(_cancel);
        FluentTheme.Apply(this);

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (!Uri.TryCreate(RepositoryUrl, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            {
                MessageBox.Show(this, "Enter a valid GitHub repository URL, for example https://github.com/owner/repo.", "Invalid repository", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
            else if (string.IsNullOrWhiteSpace(Branch))
            {
                MessageBox.Show(this, "Branch is required.", "Invalid branch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        };
    }

    private static Label MakeLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) };
}
