using System.Reflection;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class ProjectMonitorUiBootstrap
{
    private static readonly HashSet<nint> InstalledMainForms = new();
    private static ProjectMonitorDashboardForm? _dashboardForm;

    /// <summary>
    /// Explicit one-time installation owned by Program/MainForm startup. This intentionally avoids
    /// ModuleInitializer/Application.Idle scanning and post-startup tree mutation loops.
    /// </summary>
    internal static void Install(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.IsDisposed || main.Disposing)
            return;

        if (main.IsHandleCreated && InstalledMainForms.Contains(main.Handle))
            return;

        ConfigureRuntimeContext(main);
        if (!InjectProjectsButton(main))
            throw new InvalidOperationException("The canonical Projects entry could not be installed in MainForm.");

        if (main.IsHandleCreated)
            InstalledMainForms.Add(main.Handle);
    }

    private static bool InjectProjectsButton(MainForm main)
    {
        var settingsButton = FindDescendants(main)
            .OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsButton?.Parent is null) return false;
        if (FindDescendants(main).OfType<Button>().Any(b => string.Equals(b.Text, "Projects", StringComparison.OrdinalIgnoreCase))) return true;

        var button = new Button
        {
            Text = "Projects",
            AutoSize = true,
            AccessibleName = "Open Projects Hub",
            AccessibleDescription = "Open project monitoring, project state, tasks, results and New Project Monitor."
        };
        FluentTheme.StyleButton(button, primary: true);
        button.Click += (_, _) => ShowProjectsHub(main);
        settingsButton.Parent.Controls.Add(button);
        var settingsIndex = settingsButton.Parent.Controls.GetChildIndex(settingsButton);
        settingsButton.Parent.Controls.SetChildIndex(button, Math.Max(0, settingsIndex));
        return true;
    }

    private static void ConfigureRuntimeContext(MainForm main)
    {
        var (database, monitor, chrome) = GetRuntime(main);
        ProjectExecutionRuntimeContext.Configure(database, monitor, chrome);
    }

    private static (LocalDatabase Database, ChatGptMonitorService Monitor, ChromeDevToolsService Chrome) GetRuntime(MainForm main)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var database = typeof(MainForm).GetField("_database", flags)?.GetValue(main) as LocalDatabase
            ?? throw new InvalidOperationException("GPTDeskTop database is not available for the Projects Hub.");
        var monitor = typeof(MainForm).GetField("_monitor", flags)?.GetValue(main) as ChatGptMonitorService
            ?? throw new InvalidOperationException("GPTDeskTop monitor service is not available for the Projects Hub.");
        var chrome = typeof(MainForm).GetField("_chrome", flags)?.GetValue(main) as ChromeDevToolsService
            ?? throw new InvalidOperationException("GPTDeskTop Chrome service is not available for the Projects Hub.");
        return (database, monitor, chrome);
    }

    private static void ShowProjectsHub(MainForm owner)
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = new ProjectMonitorDashboardForm(() => StartNewProjectMonitorAsync(owner));
            _dashboardForm.FormClosed += (_, _) => _dashboardForm = null;
            _dashboardForm.Show(owner);
        }
        else
        {
            if (_dashboardForm.WindowState == FormWindowState.Minimized)
                _dashboardForm.WindowState = FormWindowState.Normal;
            _dashboardForm.BringToFront();
            _dashboardForm.Activate();
        }
    }

    private static async Task StartNewProjectMonitorAsync(MainForm owner)
    {
        var (database, monitor, chrome) = GetRuntime(owner);
        var wizardService = new NewProjectMonitorWizardService(database);
        IReadOnlyList<NewProjectRepositoryOption> options;
        try
        {
            options = await wizardService.LoadRepositoryOptionsAsync();
        }
        catch (Exception ex)
        {
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.LoadRepositoryOptions");
            MessageBox.Show(owner, "Saved GitHub repository profiles could not be loaded. Open Git Settings and verify the repository credentials.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowGitSettings(owner, database);
            return;
        }

        if (options.Count == 0)
        {
            MessageBox.Show(owner, "No saved GitHub repository profile is available. Configure a repository once; after that project creation is silent.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowGitSettings(owner, database);
            return;
        }

        using var wizard = new NewProjectMonitorWizardForm(options);
        if (wizard.ShowDialog(owner) != DialogResult.OK) return;

        NewProjectGitHubPreflightResult preflight;
        try
        {
            preflight = await wizardService.ValidateAsync(wizard.Draft);
        }
        catch (Exception ex)
        {
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.GitHubPreflight");
            MessageBox.Show(owner, "GitHub validation could not be completed. No ChatGPT conversation was created.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!preflight.Success)
        {
            MessageBox.Show(owner, preflight.Message + "\r\n\r\nNo ChatGPT conversation was created.", "GitHub validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (preflight.RequiresCredentialUi)
                ShowGitSettings(owner, database);
            return;
        }

        NewProjectMonitorPendingContext.Set(wizard.Draft with { Branch = preflight.Branch });
        try
        {
            var validatedDraft = NewProjectMonitorPendingContext.Take()
                ?? throw new InvalidOperationException("The validated New Project Monitor draft was lost before project creation.");
            var creator = new NewProjectMonitorCreationService(chrome, monitor, database);
            var result = await creator.ExecuteAsync(validatedDraft);
            await database.AddLogAsync(
                "System",
                $"Project monitor {result.ProjectId} created and bound to saved monitor #{result.Workflow.Monitor.Id}.",
                string.Empty,
                "NewProjectMonitorCreated",
                result.Workflow.Monitor.Id,
                result.Workflow.ConversationTab.Id,
                result.Workflow.ConversationTab.Title);
        }
        catch (Exception ex)
        {
            NewProjectMonitorPendingContext.Clear();
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.CreateProjectMonitor");
            MessageBox.Show(owner, $"The project monitor could not be created.\r\n\r\n{ex.Message}", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowGitSettings(IWin32Window owner, LocalDatabase database)
    {
        var control = new GitHubIntegrationControl(database);
        using var form = new Form
        {
            Text = "GPTDeskTop · Git Settings",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(900, 700),
            Size = new Size(1040, 820),
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = FluentTheme.Background
        };
        form.Controls.Add(control);
        form.Shown += async (_, _) =>
        {
            try { await control.LoadAsync(); }
            catch (Exception ex) { await ExceptionLogService.LogAsync(ex, "ProjectsHub.GitSettings.Load"); }
        };
        FluentTheme.Apply(form);
        form.ShowDialog(owner);
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
    }
}
