using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Exposes GitHub settings as a dedicated stable dialog. The control is never injected into an
/// already-visible SettingsForm; callers such as Projects Hub open the same dedicated dialog.
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
            ToolTipText = "Open GitHub integration settings in a dedicated stable window."
        };
        gitSettings.Click += async (_, _) => await ShowGitSettingsAsync(main, database);

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

        var database = ResolveDatabase(main);
        if (database is null)
        {
            MessageBox.Show(owner, "GitHub settings storage is not available.", "Git Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await ShowGitSettingsAsync(main, database);
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

    private static async Task ShowGitSettingsAsync(MainForm owner, LocalDatabase database)
    {
        if (owner.IsDisposed || owner.Disposing) return;
        using var dialog = BuildGitSettingsDialog(database);
        var control = dialog.Controls.Cast<Control>().SelectMany(DescendantsAndSelf).OfType<GitHubIntegrationControl>().First();
        dialog.Shown += async (_, _) =>
        {
            try { await control.LoadAsync(); }
            catch (Exception ex)
            {
                await ExceptionLogService.LogAsync(ex, "GitHubIntegrationUiBootstrap.LoadDedicatedDialog");
                if (!dialog.IsDisposed)
                    MessageBox.Show(dialog, $"GitHub settings could not be loaded.\n\n{ex.Message}", "Git Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        dialog.ShowDialog(owner);
        await Task.CompletedTask;
    }

    private static Form BuildGitSettingsDialog(LocalDatabase database)
    {
        var dialog = new Form
        {
            Text = "GPTDeskTop — Git Settings",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = true,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            MinimumSize = new Size(860, 620),
            ClientSize = new Size(980, 720),
            BackColor = FluentTheme.Background
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16), BackColor = FluentTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = FluentTheme.Background };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        header.Controls.Add(new Label { Text = "Git Settings", Dock = DockStyle.Fill, Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold), ForeColor = FluentTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        header.Controls.Add(FluentTheme.CreateMutedLabel("Configure GitHub repositories, branches and integration behavior independently from Application Settings."), 0, 1);
        var host = new Panel { Dock = DockStyle.Fill, BackColor = FluentTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12) };
        host.Controls.Add(new GitHubIntegrationControl(database) { Dock = DockStyle.Fill });
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(host, 0, 1);
        dialog.Controls.Add(root);
        FluentTheme.Apply(dialog);
        return dialog;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in Descendants(root)) yield return descendant;
    }

    private sealed class InstallationState { internal bool Installed { get; set; } }
}
