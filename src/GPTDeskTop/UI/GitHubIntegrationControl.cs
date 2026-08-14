using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class GitHubIntegrationControl : UserControl
{
    private readonly GitHubIntegrationStore _store;
    private readonly GitHubApiProbeService _probe = new();
    private readonly Dictionary<string, GitHubRepositoryCredential> _credentials = new(StringComparer.OrdinalIgnoreCase);

    private readonly RadioButton _single = new() { Text = "Single repository", Checked = true, AutoSize = true };
    private readonly RadioButton _all = new() { Text = "All accessible repositories", AutoSize = true };
    private readonly TextBox _repository = new() { PlaceholderText = "owner/repository", Dock = DockStyle.Fill };
    private readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = "main" };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "Shared / discovery github_pat_..." };
    private readonly CheckBox _showToken = new() { Text = "Show", AutoSize = true };
    private readonly CheckedListBox _repositories = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly Button _loadRepos = new() { Text = "Load Repositories", AutoSize = true };
    private readonly Button _selectAll = new() { Text = "Select All", AutoSize = true };
    private readonly Button _clearAll = new() { Text = "Clear", AutoSize = true };

    private readonly ComboBox _credentialRepository = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _credentialToken = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "Repository-specific PAT" };
    private readonly CheckBox _showCredentialToken = new() { Text = "Show", AutoSize = true };
    private readonly CheckBox _useSharedToken = new() { Text = "Use shared PAT", AutoSize = true };
    private readonly TextBox _credentialBranch = new() { Text = "main", Dock = DockStyle.Fill };
    private readonly Button _saveCredential = new() { Text = "Save Repo Credential", AutoSize = true };
    private readonly Button _clearCredential = new() { Text = "Clear Repo Credential", AutoSize = true };
    private readonly Button _testCredential = new() { Text = "Test Repo", AutoSize = true };
    private readonly Label _credentialStatus = new() { Text = "Select a repository to configure its token and branch.", AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted };

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
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Surface;
        BuildUi();
        WireEvents();
        ConfigureAccessibility();
        ApplyMode();
        foreach (var button in new[] { _test, _loadBranches, _loadRepos, _selectAll, _clearAll, _disconnect, _saveCredential, _clearCredential, _testCredential }) FluentTheme.StyleButton(button);
        FluentTheme.StyleButton(_save, primary: true);
        FluentTheme.StyleButton(_saveCredential, primary: true);
    }

    public async Task LoadAsync()
    {
        var s = await _store.LoadAsync();
        _single.Checked = !s.AllAccessibleRepositories;
        _all.Checked = s.AllAccessibleRepositories;
        _repository.Text = s.Repository;
        _branch.Text = s.Branch;
        _token.Text = s.Token;
        _watchCommits.Checked = s.WatchCommits;
        _watchPullRequests.Checked = s.WatchPullRequests;
        _watchIssues.Checked = s.WatchIssues;
        _credentials.Clear();
        foreach (var credential in s.RepositoryCredentials) _credentials[credential.Repository] = credential;
        _repositories.Items.Clear();
        foreach (var r in s.SelectedRepositories) _repositories.Items.Add(r, true);
        RebuildCredentialRepositoryList();
        ApplyMode();
        _status.Text = s.SelectedRepositories.Count == 0 && string.IsNullOrWhiteSpace(s.Repository)
            ? "GitHub is not configured."
            : $"Saved GitHub settings loaded · {_credentials.Count} repository credential(s).";
    }

    public async Task SaveAsync()
    {
        SaveCredentialEditorToMemory(showStatus: false);
        var s = ReadSettings();
        var error = Validate(s);
        if (error is not null) throw new InvalidOperationException(error);
        await _store.SaveAsync(s);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, RowCount = 16, BackColor = FluentTheme.Surface, Padding = new Padding(2) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < root.RowCount; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 6 ? 145 : i == 8 ? 170 : (i is 0 or 1 ? 38 : 52)));

        var heading = FluentTheme.CreateSectionTitle("GitHub Integration");
        root.Controls.Add(heading, 0, 0); root.SetColumnSpan(heading, 2);
        var intro = FluentTheme.CreateMutedLabel("Connect one or many repositories. A shared PAT can discover repositories, while every selected repository may have its own encrypted PAT and branch.");
        root.Controls.Add(intro, 0, 1); root.SetColumnSpan(intro, 2);
        AddRow(root, 2, "Repository scope", "Choose one repository or all repositories that GPTDeskTop should monitor.", BuildScopeHost());
        AddRow(root, 3, "Shared / discovery PAT", "Used to discover repositories and as a fallback when a repository is configured to use the shared PAT.", BuildTokenHost());
        AddRow(root, 4, "Single repository", "Used only in Single repository mode.", _repository);
        AddRow(root, 5, "Preferred branch", "Default branch used when a repository does not have an individual branch override.", _branch);
        AddRow(root, 6, "Repositories", "Load from GitHub, select the repositories to monitor, then configure credentials per repository below.", BuildRepositoriesHost());

        var credentialHeading = FluentTheme.CreateSectionTitle("Per-repository credentials");
        root.Controls.Add(credentialHeading, 0, 7); root.SetColumnSpan(credentialHeading, 2);
        AddRow(root, 8, "Repository token / branch", "Each repository can use its own PAT and branch. Tokens are encrypted for the current Windows user before saving.", BuildCredentialEditor());
        AddRow(root, 9, "Credential status", "Shows whether the selected repository has an individual credential or uses the shared PAT.", _credentialStatus);
        AddRow(root, 10, "Monitor evidence", "Choose which GitHub activity counts as project progress evidence.", BuildWatchHost());

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(4, 8, 0, 0) };
        foreach (var b in new[] { _save, _test, _loadBranches, _disconnect }) actions.Controls.Add(b);
        root.Controls.Add(actions, 0, 11); root.SetColumnSpan(actions, 2);
        var vh = FluentTheme.CreateSectionTitle("Live validation"); root.Controls.Add(vh, 0, 12); root.SetColumnSpan(vh, 2);
        root.Controls.Add(_status, 0, 13); root.SetColumnSpan(_status, 2);
        root.Controls.Add(_identity, 0, 14); root.SetColumnSpan(_identity, 2);
        root.Controls.Add(_repoInfo, 0, 15); root.SetColumnSpan(_repoInfo, 2);
        Controls.Add(root);
    }

    private Control BuildScopeHost() { var p = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; p.Controls.Add(_single); p.Controls.Add(_all); return p; }
    private Control BuildTokenHost() { var h = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); h.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); h.Controls.Add(_token, 0, 0); h.Controls.Add(_showToken, 1, 0); return h; }
    private Control BuildRepositoriesHost() { var h = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); h.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); h.Controls.Add(_repositories, 0, 0); var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown }; buttons.Controls.Add(_loadRepos); buttons.Controls.Add(_selectAll); buttons.Controls.Add(_clearAll); h.Controls.Add(buttons, 1, 0); return h; }
    private Control BuildWatchHost() { var h = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; h.Controls.Add(_watchCommits); h.Controls.Add(_watchPullRequests); h.Controls.Add(_watchIssues); return h; }

    private Control BuildCredentialEditor()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(new Label { Text = "Repository", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(_credentialRepository, 1, 0); root.SetColumnSpan(_credentialRepository, 3);
        root.Controls.Add(new Label { Text = "PAT", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(_credentialToken, 1, 1);
        root.Controls.Add(_showCredentialToken, 2, 1);
        root.Controls.Add(_useSharedToken, 3, 1);
        root.Controls.Add(new Label { Text = "Branch", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        root.Controls.Add(_credentialBranch, 1, 2); root.SetColumnSpan(_credentialBranch, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(_saveCredential); actions.Controls.Add(_testCredential); actions.Controls.Add(_clearCredential);
        root.Controls.Add(actions, 0, 3); root.SetColumnSpan(actions, 4);
        return root;
    }

    private static void AddRow(TableLayoutPanel root, int row, string title, string hint, Control control)
    {
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        labels.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold) }, 0, 0);
        labels.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, ForeColor = FluentTheme.Muted, Font = new Font("Segoe UI Variable Text", 8F), AutoEllipsis = true }, 0, 1);
        root.Controls.Add(labels, 0, row); control.Margin = new Padding(8, 7, 6, 7); root.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        _showToken.CheckedChanged += (_, _) => _token.UseSystemPasswordChar = !_showToken.Checked;
        _showCredentialToken.CheckedChanged += (_, _) => _credentialToken.UseSystemPasswordChar = !_showCredentialToken.Checked;
        _useSharedToken.CheckedChanged += (_, _) => { _credentialToken.Enabled = !_useSharedToken.Checked && !_busy; UpdateCredentialStatus(); };
        _single.CheckedChanged += (_, _) => ApplyMode(); _all.CheckedChanged += (_, _) => ApplyMode();
        _repositories.SelectedIndexChanged += (_, _) => SelectCredentialRepository(_repositories.SelectedItem?.ToString());
        _repositories.ItemCheck += (_, _) => BeginInvoke(RebuildCredentialRepositoryList);
        _credentialRepository.SelectedIndexChanged += (_, _) => LoadCredentialEditor();
        _loadRepos.Click += async (_, _) => await LoadRepositoriesAsync();
        _selectAll.Click += (_, _) => SetAllRepositories(true); _clearAll.Click += (_, _) => SetAllRepositories(false);
        _saveCredential.Click += (_, _) => SaveCredentialEditorToMemory(showStatus: true);
        _clearCredential.Click += (_, _) => ClearCredentialEditor();
        _testCredential.Click += async (_, _) => await TestRepositoryCredentialAsync();
        _save.Click += async (_, _) => await SaveFromUiAsync();
        _test.Click += async (_, _) => await TestConnectionAsync();
        _loadBranches.Click += async (_, _) => await TestConnectionAsync();
        _disconnect.Click += async (_, _) => await DisconnectAsync();
    }

    private void ApplyMode()
    {
        if (_busy) return;
        _repository.Enabled = _single.Checked; _branch.Enabled = true; _loadBranches.Enabled = _single.Checked;
        _repositories.Enabled = _all.Checked; _loadRepos.Enabled = _all.Checked; _selectAll.Enabled = _all.Checked; _clearAll.Enabled = _all.Checked;
        _credentialRepository.Enabled = _all.Checked; _credentialBranch.Enabled = _all.Checked;
        _saveCredential.Enabled = _clearCredential.Enabled = _testCredential.Enabled = _all.Checked && _credentialRepository.SelectedItem is not null;
        _credentialToken.Enabled = _all.Checked && !_useSharedToken.Checked && _credentialRepository.SelectedItem is not null;
        _useSharedToken.Enabled = _all.Checked && _credentialRepository.SelectedItem is not null;
    }

    private async Task LoadRepositoriesAsync()
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_token.Text)) { SetStatus("A shared/discovery GitHub token is required to list repositories.", false); return; }
        try
        {
            SetBusy(true, "Loading repositories from GitHub…");
            var previous = _repositories.CheckedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var repos = await _probe.ListRepositoriesAsync(_token.Text.Trim());
            _repositories.Items.Clear();
            foreach (var r in repos) _repositories.Items.Add(r.FullName, previous.Count == 0 || previous.Contains(r.FullName));
            RebuildCredentialRepositoryList();
            _repoInfo.Text = $"Repositories: {repos.Count} accessible · {_repositories.CheckedItems.Count} selected · {_credentials.Count} individual credential(s)";
            SetBusy(false, $"Loaded {repos.Count} accessible repositories. Select a repository to assign its own PAT.", true);
        }
        catch (Exception ex) { SetBusy(false, ex.Message, false); }
    }

    private void SetAllRepositories(bool value)
    {
        for (var i = 0; i < _repositories.Items.Count; i++) _repositories.SetItemChecked(i, value);
        RebuildCredentialRepositoryList();
        _repoInfo.Text = $"Repositories: {_repositories.Items.Count} accessible · {_repositories.CheckedItems.Count} selected";
    }

    private void RebuildCredentialRepositoryList()
    {
        var selected = _credentialRepository.SelectedItem?.ToString();
        var names = _repositories.CheckedItems.Cast<string>().ToList();
        if (_repositories.SelectedItem is string highlighted && _repositories.GetItemChecked(_repositories.SelectedIndex) && !names.Contains(highlighted, StringComparer.OrdinalIgnoreCase)) names.Add(highlighted);
        _credentialRepository.BeginUpdate();
        _credentialRepository.Items.Clear();
        _credentialRepository.Items.AddRange(names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        _credentialRepository.EndUpdate();
        if (selected is not null && _credentialRepository.Items.Cast<string>().Contains(selected, StringComparer.OrdinalIgnoreCase)) _credentialRepository.SelectedItem = _credentialRepository.Items.Cast<string>().First(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
        else if (_credentialRepository.Items.Count > 0) _credentialRepository.SelectedIndex = 0;
        else LoadCredentialEditor();
        ApplyMode();
    }

    private void SelectCredentialRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return;
        for (var i = 0; i < _credentialRepository.Items.Count; i++)
            if (string.Equals(_credentialRepository.Items[i]?.ToString(), repository, StringComparison.OrdinalIgnoreCase)) { _credentialRepository.SelectedIndex = i; return; }
    }

    private void LoadCredentialEditor()
    {
        var repo = _credentialRepository.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(repo))
        {
            _credentialToken.Clear(); _credentialBranch.Text = _branch.Text; _useSharedToken.Checked = true;
            _credentialStatus.Text = "Select a repository to configure its token and branch."; ApplyMode(); return;
        }
        if (_credentials.TryGetValue(repo, out var credential))
        {
            _credentialToken.Text = credential.Token; _credentialBranch.Text = credential.Branch; _useSharedToken.Checked = credential.UseSharedToken;
        }
        else
        {
            _credentialToken.Clear(); _credentialBranch.Text = _branch.Text; _useSharedToken.Checked = true;
        }
        UpdateCredentialStatus(); ApplyMode();
    }

    private void SaveCredentialEditorToMemory(bool showStatus)
    {
        var repo = _credentialRepository.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(repo)) return;
        var branch = string.IsNullOrWhiteSpace(_credentialBranch.Text) ? _branch.Text.Trim() : _credentialBranch.Text.Trim();
        var branchError = GitHubIntegrationValidator.ValidateBranch(branch);
        if (branchError is not null) { if (showStatus) SetStatus(branchError, false); return; }
        if (!_useSharedToken.Checked && string.IsNullOrWhiteSpace(_credentialToken.Text)) { if (showStatus) SetStatus($"Enter a token for {repo}, or enable Use shared PAT.", false); return; }
        _credentials[repo] = new GitHubRepositoryCredential(repo, branch, _credentialToken.Text.Trim(), _useSharedToken.Checked);
        UpdateCredentialStatus();
        if (showStatus) SetStatus($"Credential staged for {repo}. Choose Save GitHub Settings to persist it encrypted.", true);
    }

    private void ClearCredentialEditor()
    {
        var repo = _credentialRepository.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(repo)) return;
        _credentials.Remove(repo); _credentialToken.Clear(); _credentialBranch.Text = _branch.Text; _useSharedToken.Checked = true;
        UpdateCredentialStatus(); SetStatus($"Individual credential cleared for {repo}. It will use the shared PAT after saving.", true);
    }

    private void UpdateCredentialStatus()
    {
        var repo = _credentialRepository.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(repo)) { _credentialStatus.Text = "Select a repository to configure its token and branch."; return; }
        if (_credentials.TryGetValue(repo, out var c)) _credentialStatus.Text = c.UseSharedToken ? $"{repo}: using shared PAT · branch {c.Branch}" : $"{repo}: individual encrypted PAT configured · branch {c.Branch}";
        else _credentialStatus.Text = $"{repo}: using shared PAT · branch {_branch.Text}";
    }

    private async Task TestRepositoryCredentialAsync()
    {
        if (_busy) return;
        SaveCredentialEditorToMemory(showStatus: false);
        var repo = _credentialRepository.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(repo)) { SetStatus("Select a repository first.", false); return; }
        var settings = ReadSettings();
        var token = settings.ResolveToken(repo); var branch = settings.ResolveBranch(repo);
        if (string.IsNullOrWhiteSpace(token)) { SetStatus($"No token is configured for {repo}.", false); return; }
        var probeSettings = new GitHubIntegrationSettings(repo, branch, settings.WatchCommits, settings.WatchPullRequests, settings.WatchIssues, token);
        SetBusy(true, $"Testing {repo}…");
        var result = await _probe.TestAsync(probeSettings);
        _identity.Text = $"Account: {result.AuthenticatedUser ?? "—"}";
        SetBusy(false, $"{repo}: {result.Message}", result.Success);
    }

    private async Task SaveFromUiAsync()
    {
        if (_busy) return;
        try { SetBusy(true, "Saving GitHub settings and repository credentials…"); await SaveAsync(); SetBusy(false, $"GitHub settings saved securely · {_credentials.Count} repository credential(s).", true); }
        catch (Exception ex) { SetBusy(false, ex.Message, false); }
    }

    private async Task TestConnectionAsync()
    {
        if (_busy) return;
        SaveCredentialEditorToMemory(showStatus: false);
        var s = ReadSettings(); var error = Validate(s); if (error is not null) { SetStatus(error, false); return; }
        if (_all.Checked)
        {
            SetBusy(true, "Testing selected repository credentials…");
            var failures = new List<string>(); string? identity = null;
            foreach (var repo in s.SelectedRepositories)
            {
                var token = s.ResolveToken(repo); var branch = s.ResolveBranch(repo);
                var result = await _probe.TestAsync(new GitHubIntegrationSettings(repo, branch, s.WatchCommits, s.WatchPullRequests, s.WatchIssues, token));
                identity ??= result.AuthenticatedUser;
                if (!result.Success) failures.Add($"{repo}: {result.Message}");
            }
            _identity.Text = $"Account: {identity ?? "—"}";
            _repoInfo.Text = $"Repositories: {s.SelectedRepositories.Count} tested · {s.SelectedRepositories.Count - failures.Count} OK · {failures.Count} failed · {_credentials.Count} individual credential(s)";
            SetBusy(false, failures.Count == 0 ? $"All {s.SelectedRepositories.Count} selected repositories are accessible." : string.Join(" | ", failures.Take(3)), failures.Count == 0);
            return;
        }
        SetBusy(true, "Testing GitHub connection…");
        var singleResult = await _probe.TestAsync(s);
        if (singleResult.Branches.Count > 0) { var selected = _branch.Text; _branch.Items.Clear(); _branch.Items.AddRange(singleResult.Branches.Cast<object>().ToArray()); _branch.Text = selected; }
        _identity.Text = $"Account: {singleResult.AuthenticatedUser ?? "—"}";
        _repoInfo.Text = singleResult.Success || singleResult.DefaultBranch is not null ? $"Repository: {(singleResult.PrivateRepository ? "Private" : "Public")} · default branch {singleResult.DefaultBranch ?? "—"} · {singleResult.Branches.Count} branch(es)" : "Repository: —";
        SetBusy(false, singleResult.Message, singleResult.Success);
    }

    private async Task DisconnectAsync()
    {
        if (_busy) return;
        if (MessageBox.Show(this, "Remove all saved GitHub repository selections, shared PAT and per-repository encrypted PATs?", "Disconnect GitHub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        SetBusy(true, "Removing GitHub settings…"); await _store.DisconnectAsync();
        _credentials.Clear(); _single.Checked = true; _repository.Clear(); _branch.Items.Clear(); _branch.Text = "main"; _token.Clear(); _repositories.Items.Clear(); _credentialRepository.Items.Clear(); _credentialToken.Clear(); _credentialBranch.Text = "main"; _useSharedToken.Checked = true;
        _watchCommits.Checked = _watchPullRequests.Checked = _watchIssues.Checked = true; _identity.Text = "Account: —"; _repoInfo.Text = "Repositories: —"; _credentialStatus.Text = "Select a repository to configure its token and branch.";
        SetBusy(false, "GitHub disconnected. Saved shared and per-repository tokens removed.", true);
    }

    private GitHubIntegrationSettings ReadSettings() => new(_repository.Text.Trim(), _branch.Text.Trim(), _watchCommits.Checked, _watchPullRequests.Checked, _watchIssues.Checked, _token.Text.Trim())
    {
        AllAccessibleRepositories = _all.Checked,
        SelectedRepositories = _repositories.CheckedItems.Cast<string>().ToArray(),
        RepositoryCredentials = _credentials.Values.OrderBy(x => x.Repository, StringComparer.OrdinalIgnoreCase).ToArray()
    };

    private static string? Validate(GitHubIntegrationSettings s)
    {
        if (!s.AllAccessibleRepositories)
            return (string.IsNullOrWhiteSpace(s.Token) ? "GitHub token is required." : null)
                   ?? GitHubIntegrationValidator.ValidateRepository(s.Repository)
                   ?? GitHubIntegrationValidator.ValidateBranch(s.Branch)
                   ?? ValidateEvidence(s);
        if (s.SelectedRepositories.Count == 0) return "Load repositories and select at least one repository.";
        var missing = s.SelectedRepositories.Where(r => !s.HasCredentialFor(r)).Take(5).ToArray();
        if (missing.Length > 0) return $"No PAT is configured for: {string.Join(", ", missing)}. Enter a shared PAT or configure individual repository PATs.";
        return ValidateEvidence(s);
    }

    private static string? ValidateEvidence(GitHubIntegrationSettings s)
        => !s.WatchCommits && !s.WatchPullRequests && !s.WatchIssues ? "Enable at least one GitHub evidence source." : null;

    private void SetBusy(bool busy, string message, bool? success = null)
    {
        _busy = busy; _token.Enabled = _showToken.Enabled = !_busy; _single.Enabled = _all.Enabled = !_busy; _watchCommits.Enabled = _watchPullRequests.Enabled = _watchIssues.Enabled = !_busy; _save.Enabled = _test.Enabled = _disconnect.Enabled = !_busy; _showCredentialToken.Enabled = !_busy; UseWaitCursor = busy;
        if (!busy) ApplyMode(); else { _repository.Enabled = _branch.Enabled = _repositories.Enabled = _loadRepos.Enabled = _loadBranches.Enabled = _selectAll.Enabled = _clearAll.Enabled = _credentialRepository.Enabled = _credentialToken.Enabled = _credentialBranch.Enabled = _useSharedToken.Enabled = _saveCredential.Enabled = _clearCredential.Enabled = _testCredential.Enabled = false; }
        SetStatus(message, success);
    }

    private void SetStatus(string message, bool? success) { _status.Text = message; _status.ForeColor = success switch { true => Color.SeaGreen, false => Color.Firebrick, _ => FluentTheme.Accent }; }
    private void ConfigureAccessibility() { AccessibleName = "GitHub integration settings"; _single.AccessibleName = "Single GitHub repository scope"; _all.AccessibleName = "All accessible GitHub repositories scope"; _repository.AccessibleName = "GitHub repository"; _token.AccessibleName = "Shared GitHub personal access token"; _branch.AccessibleName = "GitHub preferred branch"; _repositories.AccessibleName = "GitHub repositories"; _credentialRepository.AccessibleName = "Repository credential repository"; _credentialToken.AccessibleName = "Repository specific personal access token"; _credentialBranch.AccessibleName = "Repository specific branch"; _loadRepos.AccessibleName = "Load accessible GitHub repositories"; _save.AccessibleName = "Save GitHub settings"; _test.AccessibleName = "Test GitHub connection"; _disconnect.AccessibleName = "Disconnect GitHub integration"; }
}
