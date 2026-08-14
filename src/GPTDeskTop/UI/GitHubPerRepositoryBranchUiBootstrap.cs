using System.Reflection;
using System.Runtime.CompilerServices;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

internal static class GitHubPerRepositoryBranchUiBootstrap
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
        foreach (var control in FindDescendants(form).OfType<GitHubIntegrationControl>())
        {
            if (!control.IsHandleCreated || control.IsDisposed || !Injected.Add(control.Handle)) continue;
            try { Inject(control); }
            catch (Exception ex) { _ = ExceptionLogService.LogAsync(ex, "GitHubPerRepositoryBranchUiBootstrap.Inject"); }
        }
    }

    private static void Inject(GitHubIntegrationControl control)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(GitHubIntegrationControl);
        var repositoryPicker = type.GetField("_credentialRepository", flags)?.GetValue(control) as ComboBox;
        var repoToken = type.GetField("_credentialToken", flags)?.GetValue(control) as TextBox;
        var sharedToken = type.GetField("_token", flags)?.GetValue(control) as TextBox;
        var useShared = type.GetField("_useSharedToken", flags)?.GetValue(control) as CheckBox;
        var branch = type.GetField("_credentialBranch", flags)?.GetValue(control) as TextBox;
        var status = type.GetField("_credentialStatus", flags)?.GetValue(control) as Label;
        var testButton = type.GetField("_testCredential", flags)?.GetValue(control) as Button;
        if (repositoryPicker is null || repoToken is null || sharedToken is null || useShared is null || branch is null || status is null || testButton?.Parent is null) return;

        var detect = new Button { Text = "Detect Branch", AutoSize = true, AccessibleName = "Detect repository default and main branch" };
        FluentTheme.StyleButton(detect);
        testButton.Parent.Controls.Add(detect);
        var index = testButton.Parent.Controls.GetChildIndex(testButton);
        testButton.Parent.Controls.SetChildIndex(detect, Math.Min(testButton.Parent.Controls.Count - 1, index + 1));

        detect.Click += async (_, _) =>
        {
            var repository = repositoryPicker.SelectedItem?.ToString()?.Trim() ?? string.Empty;
            var token = useShared.Checked ? sharedToken.Text.Trim() : repoToken.Text.Trim();
            if (string.IsNullOrWhiteSpace(repository))
            {
                status.Text = "Select a repository first.";
                return;
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                status.Text = useShared.Checked ? "Shared PAT is empty." : "Repository PAT is empty.";
                return;
            }

            detect.Enabled = false;
            try
            {
                var requested = string.IsNullOrWhiteSpace(branch.Text) ? "main" : branch.Text.Trim();
                var probe = new GitHubApiProbeService();
                var settings = new GitHubIntegrationSettings(repository, requested, true, true, true, token);
                var result = await probe.TestAsync(settings);
                var branches = result.Branches ?? Array.Empty<string>();
                var main = branches.FirstOrDefault(x => string.Equals(x, "main", StringComparison.Ordinal));
                var selected = main
                               ?? (!string.IsNullOrWhiteSpace(result.DefaultBranch) ? result.DefaultBranch : null)
                               ?? branches.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(selected)) branch.Text = selected;

                if (main is not null)
                    status.Text = $"main detected ✅ · Default branch: {result.DefaultBranch ?? "—"} · {branches.Count} branch(es) accessible with this repo token.";
                else if (branches.Count > 0)
                    status.Text = $"main not found · using '{selected}' · Default branch: {result.DefaultBranch ?? "—"} · {branches.Count} branch(es) accessible.";
                else
                    status.Text = result.Message;

                var saveToMemory = type.GetMethod("SaveCredentialEditorToMemory", flags);
                saveToMemory?.Invoke(control, new object[] { false });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                status.Text = ex.InnerException.Message;
                await ExceptionLogService.LogAsync(ex.InnerException, "GitHubPerRepositoryBranch.Detect");
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                await ExceptionLogService.LogAsync(ex, "GitHubPerRepositoryBranch.Detect");
            }
            finally { detect.Enabled = true; }
        };
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
