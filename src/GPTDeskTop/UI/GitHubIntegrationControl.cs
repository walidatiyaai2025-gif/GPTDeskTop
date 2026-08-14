using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class GitHubIntegrationControl : UserControl
{
    private readonly GitHubIntegrationStore _store;
    private readonly GitHubApiProbeService _probe = new();
    private readonly RadioButton _single = new() { Text = "Single repository", Checked = true, AutoSize = true };
    private readonly RadioButton _all = new() { Text = "All accessible repositories", AutoSize = true };
    private readonly TextBox _repository = new() { PlaceholderText = "owner/repository", Dock = DockStyle.Fill };
    private readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = "main" };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "github_pat_..." };
    private readonly CheckBox _showToken = new() { Text = "Show", AutoSize = true };
    private readonly CheckedListBox _repositories = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly Button _loadRepos = new() { Text = "Load Repositories", AutoSize = true };
    private readonly Button _selectAll = new() { Text = "Select All", AutoSize = true };
    private readonly Button _clearAll = new() { Text = "Clear", AutoSize = true };
    private readonly CheckBox _watchCommits = new() { Text = "Commits", Checked = true, AutoSize = true };
    private readonly CheckBox _watchPullRequests = new() { Text = "Pull requests", Checked = true, AutoSize = true };
    private readonly CheckBox _watchIssues = new() { Text = "Issues", Checked = true, AutoSize = true };
    private readonly Button _save = new() { Text = "Save GitHub Settings", AutoSize = true };
    private readonly Button _test = new() { Text = "Test Connection", AutoSize = true };
    private readonly Button _loadBranches = new() { Text = "Load Branches", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnect / Reset", AutoSize = true };
    private readonly Label _status = new() { Text = "Not tested", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private readonly Label _identity = new() { Text = "Account: —", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private readonly Label _repoInfo = new() { Text = "Repositories: —", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted };
    private bool _busy;

    public GitHubIntegrationControl(LocalDatabase database)
    {
        _store = new GitHubIntegrationStore(database ?? throw new ArgumentNullException(nameof(database)));
        Dock = DockStyle.Fill; BackColor = FluentTheme.Surface;
        BuildUi(); WireEvents(); ConfigureAccessibility(); ApplyMode();
        FluentTheme.StyleButton(_save, primary: true); FluentTheme.StyleButton(_test); FluentTheme.StyleButton(_loadBranches); FluentTheme.StyleButton(_loadRepos); FluentTheme.StyleButton(_selectAll); FluentTheme.StyleButton(_clearAll); FluentTheme.StyleButton(_disconnect);
    }

    public async Task LoadAsync()
    {
        var s = await _store.LoadAsync();
        _single.Checked = !s.AllAccessibleRepositories; _all.Checked = s.AllAccessibleRepositories;
        _repository.Text = s.Repository; _branch.Text = s.Branch; _token.Text = s.Token;
        _watchCommits.Checked = s.WatchCommits; _watchPullRequests.Checked = s.WatchPullRequests; _watchIssues.Checked = s.WatchIssues;
        _repositories.Items.Clear(); foreach (var r in s.SelectedRepositories) _repositories.Items.Add(r, true);
        ApplyMode();
        _status.Text = string.IsNullOrWhiteSpace(s.Token) ? "GitHub is not configured." : "Saved GitHub settings loaded. Use Test Connection to verify them.";
    }

    public async Task SaveAsync()
    {
        var s = ReadSettings(); var error = Validate(s); if (error is not null) throw new InvalidOperationException(error); await _store.SaveAsync(s);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, RowCount = 13, BackColor = FluentTheme.Surface, Padding = new Padding(2) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < root.RowCount; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 6 ? 150 : (i is 0 or 1 ? 38 : 52)));
        var heading = FluentTheme.CreateSectionTitle("GitHub Integration"); root.Controls.Add(heading, 0, 0); root.SetColumnSpan(heading, 2);
        var intro = FluentTheme.CreateMutedLabel("Connect one repository or every repository accessible to the PAT. Repository discovery, selection, testing and saving are all available here."); root.Controls.Add(intro, 0, 1); root.SetColumnSpan(intro, 2);
        AddRow(root, 2, "Repository scope", "Choose one repository or discover all repositories visible to this token.", BuildScopeHost());
        AddRow(root, 3, "Personal Access Token", "Use a PAT with access only to the repositories and permissions GPTDeskTop needs.", BuildTokenHost());
        AddRow(root, 4, "Single repository", "Used only in Single repository mode.", _repository);
        AddRow(root, 5, "Preferred branch", "Single mode requires this branch. All-repositories mode keeps it as the preferred branch for project matching.", _branch);
        AddRow(root, 6, "Repositories", "Load from GitHub, then select all or only the repositories GPTDeskTop should monitor.", BuildRepositoriesHost());
        AddRow(root, 7, "Monitor evidence", "Choose which GitHub activity can be used as development progress evidence.", BuildWatchHost());
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(4, 8, 0, 0) }; foreach (var b in new[] { _save, _test, _loadBranches, _disconnect }) actions.Controls.Add(b); root.Controls.Add(actions, 0, 8); root.SetColumnSpan(actions, 2);
        var vh = FluentTheme.CreateSectionTitle("Live validation"); root.Controls.Add(vh, 0, 9); root.SetColumnSpan(vh, 2);
        root.Controls.Add(_status, 0, 10); root.SetColumnSpan(_status, 2); root.Controls.Add(_identity, 0, 11); root.SetColumnSpan(_identity, 2); root.Controls.Add(_repoInfo, 0, 12); root.SetColumnSpan(_repoInfo, 2); Controls.Add(root);
    }

    private Control BuildScopeHost() { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; p.Controls.Add(_single); p.Controls.Add(_all); return p; }
    private Control BuildTokenHost() { var h = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); h.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); h.Controls.Add(_token, 0, 0); h.Controls.Add(_showToken, 1, 0); return h; }
    private Control BuildRepositoriesHost() { var h = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); h.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); h.Controls.Add(_repositories, 0, 0); var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown }; buttons.Controls.Add(_loadRepos); buttons.Controls.Add(_selectAll); buttons.Controls.Add(_clearAll); h.Controls.Add(buttons, 1, 0); return h; }
    private Control BuildWatchHost() { var h = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; h.Controls.Add(_watchCommits); h.Controls.Add(_watchPullRequests); h.Controls.Add(_watchIssues); return h; }
    private static void AddRow(TableLayoutPanel root, int row, string title, string hint, Control control) { var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; labels.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0); labels.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), AutoEllipsis = true }, 0, 1); root.Controls.Add(labels, 0, row); control.Margin = new Padding(8, 7, 6, 7); root.Controls.Add(control, 1, row); }

    private void WireEvents()
    {
        _showToken.CheckedChanged += (_, _) => _token.UseSystemPasswordChar = !_showToken.Checked;
        _single.CheckedChanged += (_, _) => ApplyMode(); _all.CheckedChanged += (_, _) => ApplyMode();
        _loadRepos.Click += async (_, _) => await LoadRepositoriesAsync(); _selectAll.Click += (_, _) => SetAllRepositories(true); _clearAll.Click += (_, _) => SetAllRepositories(false);
        _save.Click += async (_, _) => await SaveFromUiAsync(); _test.Click += async (_, _) => await TestConnectionAsync(); _loadBranches.Click += async (_, _) => await TestConnectionAsync(); _disconnect.Click += async (_, _) => await DisconnectAsync();
    }

    private void ApplyMode() { if (_busy) return; _repository.Enabled = _single.Checked; _branch.Enabled = true; _loadBranches.Enabled = _single.Checked; _repositories.Enabled = _all.Checked; _loadRepos.Enabled = _all.Checked; _selectAll.Enabled = _all.Checked; _clearAll.Enabled = _all.Checked; }
    private async Task LoadRepositoriesAsync()
    {
        if (_busy) return; if (string.IsNullOrWhiteSpace(_token.Text)) { SetStatus("GitHub token is required.", false); return; }
        try { SetBusy(true, "Loading repositories from GitHub…"); var previous = _repositories.CheckedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase); var repos = await _probe.ListRepositoriesAsync(_token.Text.Trim()); _repositories.Items.Clear(); foreach (var r in repos) _repositories.Items.Add(r.FullName, previous.Count == 0 || previous.Contains(r.FullName)); _repoInfo.Text = $"Repositories: {repos.Count} accessible · {_repositories.CheckedItems.Count} selected"; SetBusy(false, $"Loaded {repos.Count} accessible repositories.", true); }
        catch (Exception ex) { SetBusy(false, ex.Message, false); }
    }
    private void SetAllRepositories(bool value) { for (var i = 0; i < _repositories.Items.Count; i++) _repositories.SetItemChecked(i, value); _repoInfo.Text = $"Repositories: {_repositories.Items.Count} accessible · {_repositories.CheckedItems.Count} selected"; }
    private async Task SaveFromUiAsync() { if (_busy) return; try { SetBusy(true, "Saving GitHub settings…"); await SaveAsync(); SetBusy(false, "GitHub settings saved securely for this Windows user.", true); } catch (Exception ex) { SetBusy(false, ex.Message, false); } }
    private async Task TestConnectionAsync()
    {
        if (_busy) return; var s = ReadSettings(); var error = Validate(s); if (error is not null) { SetStatus(error, false); return; }
        SetBusy(true, "Testing GitHub connection…"); var result = await _probe.TestAsync(s);
        if (_single.Checked && result.Branches.Count > 0) { var selected = _branch.Text; _branch.Items.Clear(); _branch.Items.AddRange(result.Branches.Cast<object>().ToArray()); _branch.Text = selected; }
        _identity.Text = $"Account: {result.AuthenticatedUser ?? "—"}";
        _repoInfo.Text = _all.Checked ? $"Repositories: {_repositories.Items.Count} loaded · {_repositories.CheckedItems.Count} selected" : (result.Success || result.DefaultBranch is not null ? $"Repository: {(result.PrivateRepository ? "Private" : "Public")} · default branch {result.DefaultBranch ?? "—"} · {result.Branches.Count} branch(es)" : "Repository: —");
        SetBusy(false, result.Message, result.Success);
    }
    private async Task DisconnectAsync() { if (_busy) return; if (MessageBox.Show(this, "Remove all saved GitHub repository selections and the encrypted token?", "Disconnect GitHub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; SetBusy(true, "Removing GitHub settings…"); await _store.DisconnectAsync(); _single.Checked = true; _repository.Clear(); _branch.Items.Clear(); _branch.Text = "main"; _token.Clear(); _repositories.Items.Clear(); _watchCommits.Checked = _watchPullRequests.Checked = _watchIssues.Checked = true; _identity.Text = "Account: —"; _repoInfo.Text = "Repositories: —"; SetBusy(false, "GitHub disconnected. Saved token removed.", true); }

    private GitHubIntegrationSettings ReadSettings() => new(_repository.Text.Trim(), _branch.Text.Trim(), _watchCommits.Checked, _watchPullRequests.Checked, _watchIssues.Checked, _token.Text.Trim()) { AllAccessibleRepositories = _all.Checked, SelectedRepositories = _repositories.CheckedItems.Cast<string>().ToArray() };
    private static string? Validate(GitHubIntegrationSettings s) => (string.IsNullOrWhiteSpace(s.Token) ? "GitHub token is required." : null) ?? (!s.AllAccessibleRepositories ? GitHubIntegrationValidator.ValidateRepository(s.Repository) ?? GitHubIntegrationValidator.ValidateBranch(s.Branch) : (s.SelectedRepositories.Count == 0 ? "Load repositories and select at least one repository." : null)) ?? (!s.WatchCommits && !s.WatchPullRequests && !s.WatchIssues ? "Enable at least one GitHub evidence source." : null);
    private void SetBusy(bool busy, string message, bool? success = null) { _busy = busy; _token.Enabled = _showToken.Enabled = !_busy; _single.Enabled = _all.Enabled = !_busy; _watchCommits.Enabled = _watchPullRequests.Enabled = _watchIssues.Enabled = !_busy; _save.Enabled = _test.Enabled = _disconnect.Enabled = !_busy; UseWaitCursor = busy; if (!busy) ApplyMode(); else { _repository.Enabled = _branch.Enabled = _repositories.Enabled = _loadRepos.Enabled = _loadBranches.Enabled = _selectAll.Enabled = _clearAll.Enabled = false; } SetStatus(message, success); }
    private void SetStatus(string message, bool? success) { _status.Text = message; _status.ForeColor = success switch { true => Color.SeaGreen, false => Color.Firebrick, _ => FluentTheme.Accent }; }
    private void ConfigureAccessibility() { AccessibleName = "GitHub integration settings"; _single.AccessibleName = "Single GitHub repository scope"; _all.AccessibleName = "All accessible GitHub repositories scope"; _repository.AccessibleName = "GitHub repository"; _token.AccessibleName = "GitHub personal access token"; _branch.AccessibleName = "GitHub preferred branch"; _repositories.AccessibleName = "GitHub repositories"; _loadRepos.AccessibleName = "Load accessible GitHub repositories"; _save.AccessibleName = "Save GitHub settings"; _test.AccessibleName = "Test GitHub connection"; _disconnect.AccessibleName = "Disconnect GitHub integration"; }
}
