using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class GitHubIntegrationControl : UserControl
{
    private readonly GitHubIntegrationStore _store;
    private readonly GitHubApiProbeService _probe = new();
    private readonly TextBox _repository = new() { PlaceholderText = "owner/repository", Dock = DockStyle.Fill };
    private readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "github_pat_..." };
    private readonly CheckBox _showToken = new() { Text = "Show token", AutoSize = true };
    private readonly CheckBox _watchCommits = new() { Text = "Commits", Checked = true, AutoSize = true };
    private readonly CheckBox _watchPullRequests = new() { Text = "Pull requests", Checked = true, AutoSize = true };
    private readonly CheckBox _watchIssues = new() { Text = "Issues", Checked = true, AutoSize = true };
    private readonly Button _save = new() { Text = "Save GitHub Settings", AutoSize = true };
    private readonly Button _test = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button _loadBranches = new() { Text = "Load Branches", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnect / Reset", AutoSize = true };
    private readonly Label _status = new() { Text = "Not tested", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private readonly Label _identity = new() { Text = "Account: —", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private readonly Label _repoInfo = new() { Text = "Repository: —", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private bool _busy;

    public GitHubIntegrationControl(LocalDatabase database)
    {
        _store = new GitHubIntegrationStore(database ?? throw new ArgumentNullException(nameof(database)));
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Surface;
        BuildUi();
        WireEvents();
        ConfigureAccessibility();
        FluentTheme.Apply(this);
        FluentTheme.StyleButton(_save, primary: true);
        FluentTheme.StyleButton(_test);
        FluentTheme.StyleButton(_loadBranches);
        FluentTheme.StyleButton(_disconnect);
    }

    public async Task LoadAsync()
    {
        var settings = await _store.LoadAsync();
        _repository.Text = settings.Repository;
        _branch.Text = settings.Branch;
        _token.Text = settings.Token;
        _watchCommits.Checked = settings.WatchCommits;
        _watchPullRequests.Checked = settings.WatchPullRequests;
        _watchIssues.Checked = settings.WatchIssues;
        _status.Text = string.IsNullOrWhiteSpace(settings.Repository) ? "GitHub is not configured." : "Saved GitHub settings loaded. Use Test Connection to verify them.";
    }

    public async Task SaveAsync()
    {
        var settings = ReadSettings();
        var error = Validate(settings);
        if (error is not null) throw new InvalidOperationException(error);
        await _store.SaveAsync(settings);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 11,
            BackColor = FluentTheme.Surface,
            Padding = new Padding(2)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < root.RowCount; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 0 or 1 ? 38 : 52));

        var heading = FluentTheme.CreateSectionTitle("GitHub Integration");
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);
        var intro = FluentTheme.CreateMutedLabel("Configure and test Repository, PAT and Branch entirely from this UI. The PAT is encrypted for the current Windows user before it is stored.");
        root.Controls.Add(intro, 0, 1);
        root.SetColumnSpan(intro, 2);

        AddRow(root, 2, "Repository", "GitHub owner/repository, for example walidatiyaai2025-gif/GPTDeskTop.", _repository);
        AddRow(root, 3, "Personal Access Token", "Fine-grained or classic PAT with only the permissions required for this repository.", BuildTokenHost());
        AddRow(root, 4, "Branch", "Type a branch manually or use Load Branches after entering repository and token.", _branch);
        AddRow(root, 5, "Monitor evidence", "Choose which GitHub activity can be used as development progress evidence.", BuildWatchHost());

        var actionHost = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(4, 8, 0, 0) };
        actionHost.Controls.Add(_save);
        actionHost.Controls.Add(_test);
        actionHost.Controls.Add(_loadBranches);
        actionHost.Controls.Add(_disconnect);
        root.Controls.Add(actionHost, 0, 6);
        root.SetColumnSpan(actionHost, 2);

        var validationHeading = FluentTheme.CreateSectionTitle("Live validation");
        root.Controls.Add(validationHeading, 0, 7);
        root.SetColumnSpan(validationHeading, 2);
        root.Controls.Add(_status, 0, 8);
        root.SetColumnSpan(_status, 2);
        root.Controls.Add(_identity, 0, 9);
        root.SetColumnSpan(_identity, 2);
        root.Controls.Add(_repoInfo, 0, 10);
        root.SetColumnSpan(_repoInfo, 2);
        Controls.Add(root);
    }

    private Control BuildTokenHost()
    {
        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        host.Controls.Add(_token, 0, 0);
        host.Controls.Add(_showToken, 1, 0);
        return host;
    }

    private Control BuildWatchHost()
    {
        var host = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        host.Controls.Add(_watchCommits);
        host.Controls.Add(_watchPullRequests);
        host.Controls.Add(_watchIssues);
        return host;
    }

    private static void AddRow(TableLayoutPanel root, int row, string title, string hint, Control control)
    {
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        labels.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0);
        labels.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), AutoEllipsis = true }, 0, 1);
        root.Controls.Add(labels, 0, row);
        control.Margin = new Padding(8, 7, 6, 7);
        root.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        _showToken.CheckedChanged += (_, _) => _token.UseSystemPasswordChar = !_showToken.Checked;
        _save.Click += async (_, _) => await SaveFromUiAsync();
        _test.Click += async (_, _) => await TestConnectionAsync(loadBranches: true);
        _loadBranches.Click += async (_, _) => await TestConnectionAsync(loadBranches: true);
        _disconnect.Click += async (_, _) => await DisconnectAsync();
    }

    private async Task SaveFromUiAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true, "Saving GitHub settings…");
            await SaveAsync();
            SetBusy(false, "GitHub settings saved securely for this Windows user.", true);
        }
        catch (Exception ex)
        {
            SetBusy(false, ex.Message, false);
        }
    }

    private async Task TestConnectionAsync(bool loadBranches)
    {
        if (_busy) return;
        var settings = ReadSettings();
        var error = Validate(settings);
        if (error is not null)
        {
            SetStatus(error, false);
            return;
        }

        SetBusy(true, "Testing GitHub connection…");
        var result = await _probe.TestAsync(settings);
        if (loadBranches && result.Branches.Count > 0)
        {
            var selected = _branch.Text;
            _branch.BeginUpdate();
            _branch.Items.Clear();
            _branch.Items.AddRange(result.Branches.Cast<object>().ToArray());
            _branch.EndUpdate();
            _branch.Text = selected;
        }

        _identity.Text = $"Account: {result.AuthenticatedUser ?? "—"}";
        _repoInfo.Text = result.Success || result.DefaultBranch is not null
            ? $"Repository: {(result.PrivateRepository ? "Private" : "Public")} · default branch {result.DefaultBranch ?? "—"} · {result.Branches.Count} branch(es) loaded"
            : "Repository: —";
        SetBusy(false, result.Message, result.Success);
    }

    private async Task DisconnectAsync()
    {
        if (_busy) return;
        var answer = MessageBox.Show(this, "Remove the saved repository, branch and encrypted token from GPTDeskTop?", "Disconnect GitHub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        SetBusy(true, "Removing GitHub settings…");
        await _store.DisconnectAsync();
        _repository.Clear();
        _branch.Items.Clear();
        _branch.Text = "main";
        _token.Clear();
        _watchCommits.Checked = _watchPullRequests.Checked = _watchIssues.Checked = true;
        _identity.Text = "Account: —";
        _repoInfo.Text = "Repository: —";
        SetBusy(false, "GitHub disconnected. Saved token removed.", true);
    }

    private GitHubIntegrationSettings ReadSettings()
        => new(_repository.Text.Trim(), _branch.Text.Trim(), _watchCommits.Checked, _watchPullRequests.Checked, _watchIssues.Checked, _token.Text.Trim());

    private static string? Validate(GitHubIntegrationSettings settings)
        => GitHubIntegrationValidator.ValidateRepository(settings.Repository)
           ?? GitHubIntegrationValidator.ValidateBranch(settings.Branch)
           ?? (string.IsNullOrWhiteSpace(settings.Token) ? "GitHub token is required." : null)
           ?? (!settings.WatchCommits && !settings.WatchPullRequests && !settings.WatchIssues ? "Enable at least one GitHub evidence source." : null);

    private void SetBusy(bool busy, string message, bool? success = null)
    {
        _busy = busy;
        _repository.Enabled = _branch.Enabled = _token.Enabled = _showToken.Enabled = !_busy;
        _watchCommits.Enabled = _watchPullRequests.Enabled = _watchIssues.Enabled = !_busy;
        _save.Enabled = _test.Enabled = _loadBranches.Enabled = _disconnect.Enabled = !_busy;
        UseWaitCursor = busy;
        SetStatus(message, success);
    }

    private void SetStatus(string message, bool? success)
    {
        _status.Text = message;
        _status.ForeColor = success switch
        {
            true => Color.SeaGreen,
            false => Color.Firebrick,
            _ => FluentTheme.Accent
        };
    }

    private void ConfigureAccessibility()
    {
        AccessibleName = "GitHub integration settings";
        _repository.AccessibleName = "GitHub repository";
        _token.AccessibleName = "GitHub personal access token";
        _branch.AccessibleName = "GitHub branch";
        _save.AccessibleName = "Save GitHub settings";
        _test.AccessibleName = "Test GitHub connection";
        _loadBranches.AccessibleName = "Load GitHub branches";
        _disconnect.AccessibleName = "Disconnect GitHub integration";
        _status.AccessibleName = "GitHub connection status";
    }
}
