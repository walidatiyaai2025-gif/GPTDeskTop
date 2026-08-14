using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class ProjectMonitorExecutionUiBootstrap
{
    private static readonly HashSet<nint> Injected = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TryInject();
    }

    private static void TryInject()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            foreach (var dashboard in FindDescendants(form).OfType<ProjectMonitorDashboardControl>())
            {
                if (!dashboard.IsHandleCreated || dashboard.IsDisposed || !Injected.Add(dashboard.Handle)) continue;
                try { Inject(dashboard); }
                catch (Exception ex) { _ = ExceptionLogService.LogAsync(ex, "ProjectMonitorExecutionUiBootstrap.Inject"); }
            }
        }
    }

    private static void Inject(ProjectMonitorDashboardControl dashboard)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var projects = typeof(ProjectMonitorDashboardControl).GetField("_projects", flags)?.GetValue(dashboard) as DataGridView;
        if (projects is null) return;

        var header = FindDescendants(dashboard).OfType<FlowLayoutPanel>().FirstOrDefault();
        if (header is null) return;

        ProjectExecutionController? GetController()
        {
            ProjectExecutionRuntimeContext.TryConfigureFromForm(dashboard.FindForm());
            return ProjectExecutionRuntimeContext.Controller;
        }

        var monitorPicker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            AccessibleName = "Project execution monitor"
        };
        var initialize = new Button { Text = "Initialize Project", AutoSize = true };
        var start = new Button { Text = "Start / Continue Project", AutoSize = true };
        var pause = new Button { Text = "Pause", AutoSize = true };
        var stop = new Button { Text = "Stop", AutoSize = true };
        var retry = new Button { Text = "Retry", AutoSize = true };
        var runtimeStatus = new Label
        {
            Text = GetController() is null ? "Runtime unavailable" : "Ready",
            AutoSize = true,
            ForeColor = FluentTheme.Muted,
            Padding = new Padding(8, 8, 0, 0),
            AutoEllipsis = true
        };

        FluentTheme.StyleButton(initialize);
        FluentTheme.StyleButton(start, primary: true);
        FluentTheme.StyleButton(pause);
        FluentTheme.StyleButton(stop, danger: true);
        FluentTheme.StyleButton(retry);

        header.Controls.Add(new Label { Text = "Monitor", AutoSize = true, Padding = new Padding(8, 8, 0, 0), ForeColor = FluentTheme.Muted });
        header.Controls.Add(monitorPicker);
        header.Controls.Add(initialize);
        header.Controls.Add(start);
        header.Controls.Add(pause);
        header.Controls.Add(stop);
        header.Controls.Add(retry);
        header.Controls.Add(runtimeStatus);

        async Task ReloadMonitorsAsync()
        {
            monitorPicker.Items.Clear();
            var controller = GetController();
            if (controller is null)
            {
                runtimeStatus.Text = "Runtime unavailable";
                return;
            }

            try
            {
                var monitors = await controller.GetMonitorsAsync();
                foreach (var monitor in monitors.Where(x => x.Enabled).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
                    monitorPicker.Items.Add(new MonitorChoice(monitor.Id, $"#{monitor.Id} · {monitor.Title}"));
                if (monitorPicker.Items.Count > 0) monitorPicker.SelectedIndex = 0;
                runtimeStatus.Text = monitorPicker.Items.Count == 0 ? "No enabled saved monitors" : "Ready";
            }
            catch (Exception ex)
            {
                runtimeStatus.Text = "Monitor load failed";
                await ExceptionLogService.LogAsync(ex, "ProjectMonitorExecution.LoadMonitors");
            }
        }

        ProjectState? SelectedProject() => projects.CurrentRow?.Tag as ProjectState;
        long SelectedMonitorId() => monitorPicker.SelectedItem is MonitorChoice choice ? choice.Id : 0;

        void UpdateButtons()
        {
            var state = SelectedProject();
            var hasRuntime = GetController() is not null;
            var hasMonitor = SelectedMonitorId() > 0;
            var human = state?.Tasks.Any(t => t.Status == ProjectTaskStatus.AwaitingApproval) == true
                        || IsStatus(state?.Status, "WAITING_FOR_HUMAN", "HUMAN_REQUIRED", "AWAITING_APPROVAL");
            var active = IsAutomationOwnedStatus(state?.Status);
            initialize.Enabled = hasRuntime;
            start.Enabled = hasRuntime && hasMonitor && state is not null && !human && !active;
            pause.Enabled = hasRuntime && state is not null && active;
            stop.Enabled = hasRuntime && state is not null && !IsStatus(state.Status, "PROJECT_COMPLETE", "COMPLETED");
            retry.Enabled = hasRuntime && hasMonitor && state is not null && !human
                            && IsStatus(state.Status, "BLOCKED", "RECOVERING", "STALLED", "TOOL_LOOP_DETECTED", "MODEL_DELAY_TIMEOUT");
        }

        projects.SelectionChanged += (_, _) => UpdateButtons();
        monitorPicker.SelectedIndexChanged += (_, _) => UpdateButtons();

        initialize.Click += async (_, _) =>
        {
            var controller = GetController();
            if (controller is null) return;
            using var dialog = new ProjectInitializeDialog();
            if (dialog.ShowDialog(dashboard.FindForm()) != DialogResult.OK) return;
            try
            {
                SetBusy(true, "Initializing project…");
                var state = await controller.InitializeAsync(dialog.RepositoryUrl, dialog.MainGoal, dialog.Branch);
                runtimeStatus.Text = $"Initialized {state.ProjectName} · {state.CurrentBranch}";
                await dashboard.RefreshAsync(false);
            }
            catch (Exception ex)
            {
                runtimeStatus.Text = "Initialize failed";
                MessageBox.Show(dashboard.FindForm(), ex.Message, "Project initialization failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                await ExceptionLogService.LogAsync(ex, "ProjectMonitorExecution.Initialize");
            }
            finally { SetBusy(false); }
        };

        start.Click += async (_, _) =>
        {
            var controller = GetController();
            var state = SelectedProject();
            var monitorId = SelectedMonitorId();
            if (controller is null || state is null || monitorId <= 0) return;
            await ExecuteAsync("Starting project…", () => controller.StartOrContinueAsync(state, monitorId));
        };

        pause.Click += async (_, _) =>
        {
            var controller = GetController();
            var state = SelectedProject();
            if (controller is null || state is null) return;
            await ExecuteAsync("Pausing project…", () => controller.PauseAsync(state));
        };

        stop.Click += async (_, _) =>
        {
            var controller = GetController();
            var state = SelectedProject();
            if (controller is null || state is null) return;
            if (MessageBox.Show(dashboard.FindForm(), $"Stop automation for {state.ProjectName}?", "Stop project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await ExecuteAsync("Stopping project…", () => controller.StopAsync(state));
        };

        retry.Click += async (_, _) =>
        {
            var controller = GetController();
            var state = SelectedProject();
            var monitorId = SelectedMonitorId();
            if (controller is null || state is null || monitorId <= 0) return;
            await ExecuteAsync("Retrying project…", () => controller.RetryAsync(state, monitorId));
        };

        async Task ExecuteAsync(string busyText, Func<Task<ProjectExecutionResult>> action)
        {
            try
            {
                SetBusy(true, busyText);
                var result = await action();
                runtimeStatus.Text = result.Message;
                if (!result.Success)
                    MessageBox.Show(dashboard.FindForm(), result.Message, "Project automation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                await dashboard.RefreshAsync(false);
            }
            catch (Exception ex)
            {
                runtimeStatus.Text = "Operation failed";
                MessageBox.Show(dashboard.FindForm(), ex.Message, "Project automation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                await ExceptionLogService.LogAsync(ex, "ProjectMonitorExecution.Command");
            }
            finally { SetBusy(false); }
        }

        void SetBusy(bool busy, string? text = null)
        {
            monitorPicker.Enabled = !busy;
            initialize.Enabled = !busy;
            start.Enabled = !busy;
            pause.Enabled = !busy;
            stop.Enabled = !busy;
            retry.Enabled = !busy;
            if (!string.IsNullOrWhiteSpace(text)) runtimeStatus.Text = text;
            if (!busy) UpdateButtons();
        }

        _ = ReloadMonitorsAsync();
        UpdateButtons();
    }

    private static bool IsAutomationOwnedStatus(string? status) => IsStatus(status,
        "ACTIVE", "GENERATING", "WAITING_EXTERNAL", "MODEL_DELAYED_RESPONSE", "SUSPECTED_STALL",
        "VERIFYING", "RECOVERING", "ROTATING_CHAT", "RUNNING", "WAITING_FOR_REPLY");

    private static bool IsStatus(string? value, params string[] candidates) =>
        candidates.Any(x => string.Equals(value?.Trim(), x, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child)) yield return descendant;
        }
    }

    private sealed record MonitorChoice(long Id, string Label)
    {
        public override string ToString() => Label;
    }
}
