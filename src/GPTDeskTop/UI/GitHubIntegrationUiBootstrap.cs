using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Routes GitHub/Git settings into the premium single-content workspace. The underlying
/// GitHubIntegrationControl and encrypted credential store remain the only configuration path.
/// </summary>
internal static class GitHubIntegrationUiBootstrap
{
    private static readonly ConditionalWeakTable<MainForm, InstallationState> Installations = new();

    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += TryInstallIntoOpenMainForms;

    private static void TryInstallIntoOpenMainForms(object? sender, EventArgs e)
    {
        if (Application.OpenForms.Count == 0) return;
        foreach (var main in Application.OpenForms.OfType<MainForm>().ToArray())
        {
            if (main.IsDisposed || main.Disposing || !main.IsHandleCreated) continue;
            TryInstall(main);
        }
    }

    internal static bool TryInstall(MainForm main)
    {
        var state = Installations.GetValue(main, _ => new InstallationState());
        if (state.Installed) return true;

        var database = ResolveDatabase(main);
        var applicationMenu = FindApplicationMenu(main);
        if (database is null || applicationMenu is null) return false;

        if (applicationMenu.DropDownItems.OfType<ToolStripMenuItem>()
            .Any(item => string.Equals(item.Text, "Git Settings", StringComparison.OrdinalIgnoreCase)))
        {
            state.Installed = true;
            return true;
        }

        var gitSettings = new ToolStripMenuItem("Git Settings")
        {
            ToolTipText = "Open GitHub integration settings in the premium workspace."
        };
        gitSettings.Click += async (_, _) => await ShowGitSettingsAsync(main);

        var settingsIndex = applicationMenu.DropDownItems.Cast<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => x.item is ToolStripMenuItem menuItem
                                 && string.Equals(menuItem.Text, "Settings", StringComparison.OrdinalIgnoreCase))?.index ?? -1;
        if (settingsIndex >= 0 && settingsIndex < applicationMenu.DropDownItems.Count - 1)
            applicationMenu.DropDownItems.Insert(settingsIndex + 1, gitSettings);
        else
            applicationMenu.DropDownItems.Add(gitSettings);

        state.Installed = true;
        return true;
    }

    internal static async Task ShowGitSettingsAsync(IWin32Window? owner)
    {
        var main = owner as MainForm
                   ?? (owner as Form)?.Owner as MainForm
                   ?? Application.OpenForms.Cast<Form>().OfType<MainForm>().FirstOrDefault();
        if (main is null || main.IsDisposed || main.Disposing)
        {
            MessageBox.Show(owner, "The main GPTDeskTop window is not available.", "Git Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ResolveDatabase(main) is null)
        {
            MessageBox.Show(owner, "GitHub settings storage is not available.", "Git Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PremiumRuntimeShellExperience.NavigateTo(main, "GitHub / Git Settings"))
            MessageBox.Show(owner, "The premium GitHub / Git Settings workspace is not available.", "Git Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        await Task.CompletedTask;
    }

    internal static Control CreateEmbeddedGitSettingsSurface(MainForm main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var database = ResolveDatabase(main)
            ?? throw new InvalidOperationException("GitHub settings storage is not available.");
        return new EmbeddedGitSettingsSurface(database);
    }

    private static LocalDatabase? ResolveDatabase(MainForm main)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return typeof(MainForm).GetField("_database", flags)?.GetValue(main) as LocalDatabase;
    }

    private static ToolStripMenuItem? FindApplicationMenu(MainForm main)
    {
        foreach (var strip in Descendants(main).OfType<MenuStrip>())
            foreach (var item in strip.Items.OfType<ToolStripMenuItem>())
            {
                var application = FindMenuRecursive(item, "Application");
                if (application is not null) return application;
            }
        return null;
    }

    private static ToolStripMenuItem? FindMenuRecursive(ToolStripMenuItem root, string text)
    {
        if (string.Equals(root.Text, text, StringComparison.OrdinalIgnoreCase)) return root;
        foreach (var child in root.DropDownItems.OfType<ToolStripMenuItem>())
        {
            var match = FindMenuRecursive(child, text);
            if (match is not null) return match;
        }
        return null;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class InstallationState { internal bool Installed { get; set; } }

    private sealed class EmbeddedGitSettingsSurface : UserControl
    {
        private readonly GitHubIntegrationControl _control;
        private readonly Label _loadState = new()
        {
            Text = "Loading saved GitHub configuration…",
            Dock = DockStyle.Fill,
            ForeColor = FluentTheme.Muted,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true
        };
        private bool _loaded;
        private bool _loading;

        internal EmbeddedGitSettingsSurface(LocalDatabase database)
        {
            Name = "PremiumGitSettingsWorkspace";
            AccessibleName = "GitHub and Git Settings workspace";
            AccessibleDescription = "Repository, branch and encrypted GitHub credential settings on the single premium content surface.";
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = FluentTheme.Background;
            Padding = new Padding(14);

            _control = new GitHubIntegrationControl(database) { Dock = DockStyle.Fill };
            BuildUi();
            VisibleChanged += async (_, _) =>
            {
                if (Visible) await EnsureLoadedAsync();
            };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = FluentTheme.Background,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = FluentTheme.Background, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            header.Controls.Add(new Label
            {
                Text = "GitHub / Git Settings",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
                ForeColor = FluentTheme.Text,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            header.Controls.Add(_loadState, 1, 0);
            var subtitle = FluentTheme.CreateMutedLabel("Configure real repository scope, branch context and protected credentials without leaving the main product surface.");
            header.Controls.Add(subtitle, 0, 1);
            header.SetColumnSpan(subtitle, 2);

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FluentTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(12),
                Margin = new Padding(0, 4, 0, 0)
            };
            host.Controls.Add(_control);
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(host, 0, 1);
            Controls.Add(root);
        }

        private async Task EnsureLoadedAsync()
        {
            if (_loaded || _loading || IsDisposed || Disposing) return;
            _loading = true;
            try
            {
                await _control.LoadAsync();
                _loaded = true;
                _loadState.Text = $"Saved configuration loaded · {DateTime.Now:t}";
                _loadState.ForeColor = FluentTheme.Success;
            }
            catch (Exception ex)
            {
                await ExceptionLogService.LogAsync(ex, "GitHubIntegrationUiBootstrap.LoadEmbeddedWorkspace");
                _loadState.Text = "Saved configuration could not be loaded.";
                _loadState.ForeColor = FluentTheme.Danger;
            }
            finally
            {
                _loading = false;
            }
        }
    }
}
