using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

public sealed class ProjectMonitorDashboardControl : UserControl
{
    private readonly ProjectStateStore _store = new();
    private readonly DataGridView _projects = new();
    private readonly DataGridView _tasks = new();
    private readonly RichTextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = FluentTheme.Surface };
    private readonly Button _newProject = new() { Text = "New Project Monitor", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly CheckBox _autoRefresh = new() { Text = "Auto refresh", Checked = true, AutoSize = true };
    private readonly Label _summary = new() { Text = "No project state loaded.", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = FluentTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly List<ProjectState> _states = new();
    private readonly Func<Task>? _startNewProjectMonitor;
    private bool _loading;
    private bool _startingProject;

    public ProjectMonitorDashboardControl(Func<Task>? startNewProjectMonitor = null)
    {
        _startNewProjectMonitor = startNewProjectMonitor;
        Dock = DockStyle.Fill;
        BackColor = FluentTheme.Background;
        ConfigureProjectsGrid();
        ConfigureTasksGrid();
        BuildUi();
        FluentTheme.StyleButton(_newProject, primary: true);
        FluentTheme.StyleButton(_refresh);
        _newProject.Enabled = _startNewProjectMonitor is not null;
        _newProject.Click += async (_, _) => await StartNewProjectMonitorAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _autoRefresh.CheckedChanged += (_, _) => _timer.Enabled = _autoRefresh.Checked;
        _timer.Tick += async (_, _) => await RefreshAsync(preserveSelection: true);
        _projects.SelectionChanged += (_, _) => RenderSelectedProject();
        VisibleChanged += async (_, _) =>
        {
            if (Visible && !_loading)
            {
                await RefreshAsync();
                _timer.Enabled = _autoRefresh.Checked;
            }
            else if (!Visible) _timer.Enabled = false;
        };
        Disposed += (_, _) => _timer.Dispose();
    }

    private async Task StartNewProjectMonitorAsync()
    {
        if (_startNewProjectMonitor is null || _startingProject) return;
        _startingProject = true;
        _newProject.Enabled = false;
        try
        {
            await _startNewProjectMonitor();
            await RefreshAsync(preserveSelection: false);
        }
        finally
        {
            _startingProject = false;
            _newProject.Enabled = true;
        }
    }

    public async Task RefreshAsync(bool preserveSelection = true)
    {
        if (_loading) return;
        _loading = true;
        var selectedId = preserveSelection && _projects.CurrentRow?.Tag is ProjectState current ? current.ProjectId : null;
        try
        {
            var states = new List<ProjectState>();
            foreach (var id in _store.ListProjectIds())
            {
                try
                {
                    var state = await _store.LoadAsync(id);
                    if (state is not null) states.Add(state);
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, $"ProjectMonitorDashboard.Load.{id}");
                }
            }

            _states.Clear();
            _states.AddRange(states.OrderByDescending(x => x.UpdatedAt));
            _projects.Rows.Clear();
            foreach (var state in _states)
            {
                var p = ProjectProgressService.Calculate(state);
                var active = p.InProgress + p.Verifying;
                var result = BuildResult(state, p);
                var rowIndex = _projects.Rows.Add(
                    DisplayName(state),
                    NormalizeStatus(state.Status),
                    state.CurrentPhase,
                    $"{p.PercentComplete:0.#}%",
                    $"{p.Completed}/{p.Total}",
                    active,
                    p.Blocked,
                    p.AwaitingApproval,
                    state.HealthScore,
                    state.CurrentBranch,
                    EmptyAsDash(state.CurrentPR),
                    ShortSha(state.LastCommit),
                    result,
                    state.UpdatedAt.ToLocalTime().ToString("g"));
                var row = _projects.Rows[rowIndex];
                row.Tag = state;
                ApplyStatusColor(row, state, p);
            }

            var totals = _states.Select(ProjectProgressService.Calculate).ToArray();
            _summary.Text = _states.Count == 0
                ? "No projects yet. Choose New Project Monitor to create a fresh ChatGPT conversation and start monitoring it."
                : $"Projects: {_states.Count} · Tasks: {totals.Sum(x => x.Total)} · Completed: {totals.Sum(x => x.Completed)} · Active: {totals.Sum(x => x.InProgress + x.Verifying)} · Blocked: {totals.Sum(x => x.Blocked)} · Human approval: {totals.Sum(x => x.AwaitingApproval)}";

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                foreach (DataGridViewRow row in _projects.Rows)
                    if (row.Tag is ProjectState state && string.Equals(state.ProjectId, selectedId, StringComparison.OrdinalIgnoreCase))
                    { row.Selected = true; _projects.CurrentCell = row.Cells[0]; break; }
            }
            if (_projects.CurrentRow is null && _projects.Rows.Count > 0) _projects.CurrentCell = _projects.Rows[0].Cells[0];
            RenderSelectedProject();
        }
        finally { _loading = false; }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), BackColor = FluentTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = FluentTheme.Background, AutoScroll = true };
        header.Controls.Add(FluentTheme.CreateSectionTitle("Projects"));
        header.Controls.Add(_newProject);
        header.Controls.Add(_refresh);
        header.Controls.Add(_autoRefresh);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_summary, 0, 1);

        root.Controls.Add(CreatePanel("All project monitors — status, progress and latest result", _projects), 0, 2);
        var bottom = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6, BackColor = FluentTheme.Background };
        bottom.Panel1.Padding = new Padding(0, 6, 4, 0);
        bottom.Panel2.Padding = new Padding(4, 6, 0, 0);
        bottom.Panel1.Controls.Add(CreatePanel("Selected project tasks", _tasks));
        bottom.Panel2.Controls.Add(CreatePanel("Monitor result / evidence", _details));
        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);
    }

    private static Control CreatePanel(string title, Control body)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(10), BackColor = FluentTheme.Surface, Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(FluentTheme.CreateSectionTitle(title), 0, 0); panel.Controls.Add(body, 0, 1); return panel;
    }

    private void ConfigureProjectsGrid()
    {
        ConfigureGrid(_projects);
        AddColumn(_projects, "Project", 170);
        AddColumn(_projects, "Status", 95);
        AddColumn(_projects, "Phase", 110);
        AddColumn(_projects, "Progress", 75);
        AddColumn(_projects, "Tasks", 70);
        AddColumn(_projects, "Active", 55);
        AddColumn(_projects, "Blocked", 60);
        AddColumn(_projects, "Human", 55);
        AddColumn(_projects, "Health", 55);
        AddColumn(_projects, "Branch", 90);
        AddColumn(_projects, "PR", 65);
        AddColumn(_projects, "Commit", 80);
        AddColumn(_projects, "Latest result", 260, DataGridViewAutoSizeColumnMode.Fill);
        AddColumn(_projects, "Updated", 120);
    }

    private void ConfigureTasksGrid()
    {
        ConfigureGrid(_tasks);
        AddColumn(_tasks, "Task", 85);
        AddColumn(_tasks, "Title", 210, DataGridViewAutoSizeColumnMode.Fill);
        AddColumn(_tasks, "Status", 95);
        AddColumn(_tasks, "Priority", 70);
        AddColumn(_tasks, "Issue", 55);
        AddColumn(_tasks, "PR", 55);
        AddColumn(_tasks, "Commit", 80);
        AddColumn(_tasks, "Result / blocked reason", 220);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.RowHeadersVisible = false; grid.AutoGenerateColumns = false;
        grid.BackgroundColor = FluentTheme.Surface; grid.BorderStyle = BorderStyle.None;
    }

    private static void AddColumn(DataGridView grid, string header, int width, DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None)
        => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, Width = width, AutoSizeMode = mode, SortMode = DataGridViewColumnSortMode.Automatic });

    private void RenderSelectedProject()
    {
        if (_projects.CurrentRow?.Tag is not ProjectState state)
        {
            _tasks.Rows.Clear(); _details.Text = "Select a project to inspect monitor results, errors, decisions and GitHub evidence."; return;
        }

        _tasks.Rows.Clear();
        foreach (var task in state.Tasks.OrderBy(x => TaskOrder(x.Status)).ThenBy(x => x.TaskId, StringComparer.OrdinalIgnoreCase))
        {
            var evidence = task.VerificationEvidence.LastOrDefault();
            var result = !string.IsNullOrWhiteSpace(task.BlockedReason) ? task.BlockedReason : evidence ?? "—";
            var rowIndex = _tasks.Rows.Add(task.TaskId, task.Title, task.Status, task.Priority, task.GitHubIssue?.ToString() ?? "—", task.GitHubPR?.ToString() ?? "—", ShortSha(task.LastCommit), result);
            if (task.Status == ProjectTaskStatus.Blocked) _tasks.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.Firebrick;
            else if (task.Status == ProjectTaskStatus.Completed) _tasks.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.SeaGreen;
            else if (task.Status == ProjectTaskStatus.AwaitingApproval) _tasks.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.DarkOrange;
        }

        var p = ProjectProgressService.Calculate(state);
        var current = ProjectProgressService.CurrentWork(state);
        var problems = ProjectProgressService.Problems(state);
        var lines = new List<string>
        {
            $"Project: {DisplayName(state)}",
            $"Repository: {EmptyAsDash(state.RepoUrl)}",
            $"Status: {NormalizeStatus(state.Status)}   Phase: {EmptyAsDash(state.CurrentPhase)}   Health: {state.HealthScore}/100",
            $"Progress: {p.Completed}/{p.Total} completed ({p.PercentComplete:0.#}%) · {p.InProgress} running · {p.Verifying} verifying · {p.Blocked} blocked · {p.AwaitingApproval} awaiting human",
            $"Current work: {(current is null ? "—" : $"{current.TaskId} · {current.Title}")}",
            $"Branch: {EmptyAsDash(state.CurrentBranch)}   PR: {EmptyAsDash(state.CurrentPR)}   Last commit: {EmptyAsDash(state.LastCommit)}",
            $"Current chat: {EmptyAsDash(state.CurrentChatId)}   Chat generation: {state.ChatGeneration}",
            $"Last verified: {(state.LastVerifiedAt?.ToLocalTime().ToString("G") ?? "—")}",
            $"Updated: {state.UpdatedAt.ToLocalTime():G}",
            "",
            "LATEST MONITOR RESULT",
            BuildResult(state, p),
            "",
            "NEXT ACTION",
            EmptyAsDash(state.NextAction),
            "",
            $"PROBLEMS ({problems.Count})"
        };
        lines.AddRange(problems.Select(x => $"• {x.TaskId} {x.Title}: {(!string.IsNullOrWhiteSpace(x.BlockedReason) ? x.BlockedReason : x.Status.ToString())}"));
        if (problems.Count == 0) lines.Add("• None");
        lines.Add(""); lines.Add($"KNOWN ERRORS ({state.KnownErrors.Count})"); lines.AddRange(state.KnownErrors.Select(x => "• " + x)); if (state.KnownErrors.Count == 0) lines.Add("• None");
        lines.Add(""); lines.Add($"IMPORTANT DECISIONS ({state.ImportantDecisions.Count})"); lines.AddRange(state.ImportantDecisions.TakeLast(10).Select(x => "• " + x)); if (state.ImportantDecisions.Count == 0) lines.Add("• None");
        var evidenceItems = state.Tasks.SelectMany(t => t.VerificationEvidence.Select(e => $"• {t.TaskId}: {e}")).TakeLast(15).ToArray();
        lines.Add(""); lines.Add($"LATEST VERIFICATION EVIDENCE ({evidenceItems.Length})"); lines.AddRange(evidenceItems); if (evidenceItems.Length == 0) lines.Add("• None");
        _details.Text = string.Join(Environment.NewLine, lines);
    }

    private static string BuildResult(ProjectState state, ProjectProgress p)
    {
        if (state.KnownErrors.Count > 0) return $"ERROR: {state.KnownErrors.Last()}";
        var blocked = state.Tasks.LastOrDefault(x => x.Status == ProjectTaskStatus.Blocked && !string.IsNullOrWhiteSpace(x.BlockedReason));
        if (blocked is not null) return $"BLOCKED {blocked.TaskId}: {blocked.BlockedReason}";
        var approval = state.Tasks.LastOrDefault(x => x.Status == ProjectTaskStatus.AwaitingApproval);
        if (approval is not null) return $"HUMAN REQUIRED: {approval.TaskId} {approval.Title}";
        if (!string.IsNullOrWhiteSpace(state.NextAction)) return state.NextAction;
        if (p.Total > 0 && p.Completed == p.Total) return "All tracked tasks completed.";
        return string.IsNullOrWhiteSpace(state.Status) ? "Monitoring" : state.Status;
    }

    private static string DisplayName(ProjectState state) => !string.IsNullOrWhiteSpace(state.ProjectName) ? state.ProjectName : state.ProjectId;
    private static string NormalizeStatus(string? status) => string.IsNullOrWhiteSpace(status) ? "IDLE" : status.Trim().ToUpperInvariant();
    private static string EmptyAsDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string ShortSha(string? sha) => string.IsNullOrWhiteSpace(sha) ? "—" : sha.Length <= 8 ? sha : sha[..8];
    private static int TaskOrder(ProjectTaskStatus status) => status switch { ProjectTaskStatus.InProgress => 0, ProjectTaskStatus.Verifying => 1, ProjectTaskStatus.Blocked => 2, ProjectTaskStatus.AwaitingApproval => 3, ProjectTaskStatus.Ready => 4, ProjectTaskStatus.Discovered => 5, ProjectTaskStatus.Completed => 6, _ => 7 };

    private static void ApplyStatusColor(DataGridViewRow row, ProjectState state, ProjectProgress p)
    {
        if (state.KnownErrors.Count > 0 || p.Blocked > 0) row.DefaultCellStyle.ForeColor = Color.Firebrick;
        else if (p.AwaitingApproval > 0) row.DefaultCellStyle.ForeColor = Color.DarkOrange;
        else if (p.Total > 0 && p.Completed == p.Total) row.DefaultCellStyle.ForeColor = Color.SeaGreen;
    }
}
