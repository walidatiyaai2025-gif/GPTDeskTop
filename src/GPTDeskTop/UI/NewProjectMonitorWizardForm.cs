using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class NewProjectMonitorWizardForm : Form
{
    private readonly ComboBox _repository = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _branch = new() { Dock = DockStyle.Fill, Text = "main" };
    private readonly TextBox _instruction = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _monitorReply = new() { Dock = DockStyle.Fill, Text = "كمل" };
    private readonly Label _credentialState = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private readonly Button _start = new() { Text = "Validate & Start", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Dictionary<string, NewProjectRepositoryOption> _options;

    public NewProjectMonitorWizardForm(IReadOnlyList<NewProjectRepositoryOption> options)
    {
        _options = options.ToDictionary(x => x.Repository, StringComparer.OrdinalIgnoreCase);
        Text = "New Project Monitor";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 520);
        Size = new Size(820, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FluentTheme.Background;
        BuildUi();

        foreach (var option in options) _repository.Items.Add(option.Repository);
        if (_repository.Items.Count > 0) _repository.SelectedIndex = 0;
        _repository.SelectedIndexChanged += (_, _) => ApplyRepositoryDefaults();
        _start.Click += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(_repository.Text) || string.IsNullOrWhiteSpace(_instruction.Text))
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(this, "Choose a repository and enter the project instruction.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        ApplyRepositoryDefaults();
        AcceptButton = _start;
        CancelButton = _cancel;
        FluentTheme.Apply(this);
    }

    public NewProjectMonitorDraft Draft => new(
        _repository.Text.Trim(),
        _branch.Text.Trim(),
        _instruction.Text.Trim(),
        string.IsNullOrWhiteSpace(_monitorReply.Text) ? "كمل" : _monitorReply.Text.Trim());

    private void ApplyRepositoryDefaults()
    {
        if (!_options.TryGetValue(_repository.Text, out var option)) return;
        _branch.Text = string.IsNullOrWhiteSpace(option.SuggestedBranch) ? "main" : option.SuggestedBranch;
        _credentialState.Text = option.HasSavedToken
            ? "GitHub authentication: saved token available — validation will run silently."
            : "GitHub authentication: action required — Git Settings will open only if validation needs credentials.";
        _credentialState.ForeColor = option.HasSavedToken ? Color.SeaGreen : Color.DarkOrange;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(18), BackColor = FluentTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        root.Controls.Add(FluentTheme.CreateSectionTitle("New Project Monitor"), 0, 0);
        root.Controls.Add(Field("Repository", "Saved GitHub repository profile.", _repository), 0, 1);
        root.Controls.Add(Field("Branch", "Automatically starts with the repository default branch when available.", _branch), 0, 2);
        root.Controls.Add(FluentTheme.CreateSectionTitle("Project instruction"), 0, 3);
        root.Controls.Add(_instruction, 0, 4);
        root.Controls.Add(Field("Monitor reply", "Continuation message used after a completed ChatGPT response.", _monitorReply), 0, 5);
        root.Controls.Add(_credentialState, 0, 6);
        root.Controls.Add(FluentTheme.CreateMutedLabel("No token is shown here. Saved per-repository credentials are resolved and validated silently before any new ChatGPT conversation is created."), 0, 7);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        FluentTheme.StyleButton(_start, primary: true); FluentTheme.StyleButton(_cancel);
        actions.Controls.Add(_start); actions.Controls.Add(_cancel);
        root.Controls.Add(actions, 0, 8);
        Controls.Add(root);
    }

    private static Control Field(string title, string hint, Control input)
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        host.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0);
        host.Controls.Add(input, 1, 0); host.SetRowSpan(input, 2);
        host.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, AutoEllipsis = true }, 0, 1);
        return host;
    }
}
