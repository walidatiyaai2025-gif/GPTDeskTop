using System.Diagnostics;
using System.Reflection;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class ProjectMonitorUiBootstrap
{
    private static readonly HashSet<nint> InstalledMainForms = new();
    private static RuntimeInspectorForm? _runtimeInspectorForm;

    /// <summary>
    /// Explicit one-time installation owned by Program/MainForm startup. Projects are routed into
    /// the premium content host; Runtime Inspector retains its existing canonical diagnostic path.
    /// </summary>
    internal static void Install(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        if (main.IsDisposed || main.Disposing) return;
        if (main.IsHandleCreated && InstalledMainForms.Contains(main.Handle)) return;

        ConfigureRuntimeContext(main);
        RetireDuplicateMonitorNavigation(main);
        if (!InjectProjectsButton(main))
            throw new InvalidOperationException("The canonical Projects entry could not be installed in MainForm.");
        InjectRuntimeInspectorButton(main);

        if (main.IsHandleCreated) InstalledMainForms.Add(main.Handle);
    }

    internal static Control CreateEmbeddedProjectsSurface(MainForm owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new ProjectMonitorDashboardControl(() => StartNewProjectMonitorAsync(owner))
        {
            Name = "PremiumProjectsWorkspace",
            AccessibleName = "Projects workspace",
            AccessibleDescription = "Registered projects, real repository and branch context, task progress and monitoring evidence."
        };
    }

    private static bool InjectProjectsButton(MainForm main)
    {
        var settingsButton = FindDescendants(main).OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsButton?.Parent is null) return false;
        if (FindDescendants(main).OfType<Button>().Any(b => string.Equals(b.Text, "Projects", StringComparison.OrdinalIgnoreCase))) return true;

        var version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location).ProductVersion ?? "unknown";
        var button = new Button
        {
            Text = "Projects",
            AutoSize = true,
            AccessibleName = "Open Projects Hub",
            AccessibleDescription = $"Open the canonical project monitoring workspace. Running build: {version}."
        };
        FluentTheme.StyleButton(button, primary: true);
        button.Click += (_, _) => PremiumRuntimeShellExperience.NavigateTo(main, "Projects");
        settingsButton.Parent.Controls.Add(button);
        var settingsIndex = settingsButton.Parent.Controls.GetChildIndex(settingsButton);
        settingsButton.Parent.Controls.SetChildIndex(button, Math.Max(0, settingsIndex));
        return true;
    }

    private static void InjectRuntimeInspectorButton(MainForm main)
    {
        var settingsButton = FindDescendants(main).OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Settings", StringComparison.OrdinalIgnoreCase));
        if (settingsButton?.Parent is null) return;
        if (FindDescendants(main).OfType<Button>().Any(b => string.Equals(b.Text, "Runtime Inspector", StringComparison.OrdinalIgnoreCase))) return;

        var button = new Button
        {
            Text = "Runtime Inspector",
            AutoSize = true,
            AccessibleName = "Open Runtime Inspector",
            AccessibleDescription = "Inspect the actual running EXE, monitors, browser processes and visible UI tree, or export a support bundle."
        };
        FluentTheme.StyleButton(button, primary: false);
        button.Click += (_, _) => ShowRuntimeInspector(main);
        settingsButton.Parent.Controls.Add(button);
        var settingsIndex = settingsButton.Parent.Controls.GetChildIndex(settingsButton);
        settingsButton.Parent.Controls.SetChildIndex(button, Math.Max(0, settingsIndex));
    }

    private static void RetireDuplicateMonitorNavigation(MainForm main)
    {
        foreach (var button in FindDescendants(main).OfType<Button>().ToArray())
        {
            if (string.Equals(button.Text, "Monitors", StringComparison.OrdinalIgnoreCase)
                || string.Equals(button.Text, "Saved Monitors", StringComparison.OrdinalIgnoreCase))
            {
                button.Visible = false;
                button.TabStop = false;
                button.AccessibleDescription = "Legacy navigation retired; use the premium Saved Monitors destination.";
            }
        }

        foreach (var menu in FindToolStripItems(main).ToArray())
        {
            if (string.Equals(menu.Text, "Monitors", StringComparison.OrdinalIgnoreCase)
                || string.Equals(menu.Text, "Saved Monitors", StringComparison.OrdinalIgnoreCase))
                menu.Visible = false;
        }
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

    private static void ShowRuntimeInspector(MainForm owner)
    {
        var (_, monitor, _) = GetRuntime(owner);
        if (_runtimeInspectorForm is null || _runtimeInspectorForm.IsDisposed)
        {
            _runtimeInspectorForm = new RuntimeInspectorForm(owner, monitor);
            _runtimeInspectorForm.FormClosed += (_, _) => _runtimeInspectorForm = null;
            _runtimeInspectorForm.Show(owner);
        }
        else
        {
            if (_runtimeInspectorForm.WindowState == FormWindowState.Minimized) _runtimeInspectorForm.WindowState = FormWindowState.Normal;
            _runtimeInspectorForm.BringToFront();
            _runtimeInspectorForm.Activate();
        }
    }

    private static async Task StartNewProjectMonitorAsync(MainForm owner)
    {
        var (database, monitor, chrome) = GetRuntime(owner);
        var wizardService = new NewProjectMonitorWizardService(database);
        IReadOnlyList<NewProjectRepositoryOption> options;
        try { options = await wizardService.LoadRepositoryOptionsAsync(); }
        catch (Exception ex)
        {
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.LoadRepositoryOptions");
            MessageBox.Show(owner, "Saved GitHub repository profiles could not be loaded. Open Git Settings and verify the repository credentials.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowGitSettings(owner); return;
        }

        if (options.Count == 0)
        {
            MessageBox.Show(owner, "No saved GitHub repository profile is available. Configure a repository once; after that project creation is silent.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowGitSettings(owner); return;
        }

        using var wizard = new NewProjectMonitorWizardForm(options);
        if (wizard.ShowDialog(owner) != DialogResult.OK) return;

        NewProjectGitHubPreflightResult preflight;
        try { preflight = await wizardService.ValidateAsync(wizard.Draft); }
        catch (Exception ex)
        {
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.GitHubPreflight");
            MessageBox.Show(owner, "GitHub validation could not be completed. No ChatGPT conversation was created.", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }

        if (!preflight.Success)
        {
            MessageBox.Show(owner, preflight.Message + "\r\n\r\nNo ChatGPT conversation was created.", "GitHub validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (preflight.RequiresCredentialUi) ShowGitSettings(owner);
            return;
        }

        NewProjectMonitorPendingContext.Set(wizard.Draft with { Branch = preflight.Branch });
        try
        {
            var validatedDraft = NewProjectMonitorPendingContext.Take() ?? throw new InvalidOperationException("The validated New Project Monitor draft was lost before project creation.");
            var creator = new NewProjectMonitorCreationService(chrome, monitor, database);
            var result = await creator.ExecuteAsync(validatedDraft);
            await database.AddLogAsync("System", $"Project monitor {result.ProjectId} created and bound to saved monitor #{result.Workflow.Monitor.Id}.", string.Empty, "NewProjectMonitorCreated", result.Workflow.Monitor.Id, result.Workflow.ConversationTab.Id, result.Workflow.ConversationTab.Title);
        }
        catch (Exception ex)
        {
            NewProjectMonitorPendingContext.Clear();
            await ExceptionLogService.LogAsync(ex, "ProjectsHub.CreateProjectMonitor");
            MessageBox.Show(owner, $"The project monitor could not be created.\r\n\r\n{ex.Message}", "New Project Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowGitSettings(MainForm owner)
        => _ = GitHubIntegrationUiBootstrap.ShowGitSettingsAsync(owner);

    private static IEnumerable<ToolStripItem> FindToolStripItems(Control root)
    {
        foreach (var strip in FindDescendants(root).OfType<ToolStrip>())
            foreach (ToolStripItem item in strip.Items)
                foreach (var descendant in Flatten(item)) yield return descendant;
    }

    private static IEnumerable<ToolStripItem> Flatten(ToolStripItem item)
    {
        yield return item;
        if (item is ToolStripDropDownItem dropdown)
            foreach (ToolStripItem child in dropdown.DropDownItems)
                foreach (var nested in Flatten(child)) yield return nested;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindDescendants(child)) yield return descendant;
        }
    }
}
