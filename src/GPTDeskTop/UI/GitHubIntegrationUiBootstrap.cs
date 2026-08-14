using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

internal static class GitHubIntegrationUiBootstrap
{
    private static readonly HashSet<nint> Injected = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => TryInjectIntoOpenSettingsForms();
    }

    private static void TryInjectIntoOpenSettingsForms()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form is not SettingsForm settings || settings.IsDisposed || !settings.IsHandleCreated) continue;
            if (!Injected.Add(settings.Handle)) continue;

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var tabs = typeof(SettingsForm).GetField("_tabs", flags)?.GetValue(settings) as TabControl;
                var database = typeof(SettingsForm).GetField("_database", flags)?.GetValue(settings) as LocalDatabase;
                if (tabs is null || database is null) continue;
                if (tabs.TabPages.Cast<TabPage>().Any(p => string.Equals(p.Text, "GitHub", StringComparison.OrdinalIgnoreCase))) continue;

                var page = new TabPage("GitHub")
                {
                    BackColor = FluentTheme.Surface,
                    Padding = new Padding(18)
                };
                var control = new GitHubIntegrationControl(database) { Dock = DockStyle.Fill };
                page.Controls.Add(control);
                tabs.TabPages.Add(page);
                _ = LoadSafelyAsync(control);
            }
            catch (Exception ex)
            {
                _ = ExceptionLogService.LogAsync(ex, "GitHubIntegrationUiBootstrap.Inject");
            }
        }
    }

    private static async Task LoadSafelyAsync(GitHubIntegrationControl control)
    {
        try { await control.LoadAsync(); }
        catch (Exception ex) { await ExceptionLogService.LogAsync(ex, "GitHubIntegrationUiBootstrap.Load"); }
    }
}
