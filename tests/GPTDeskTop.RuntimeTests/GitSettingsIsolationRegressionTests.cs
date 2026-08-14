namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsIsolationRegressionTests
{
    [Fact]
    public void GitHubSettingsAreIsolatedFromApplicationSettingsControlTree()
    {
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("Git Settings", bootstrap, StringComparison.Ordinal);
        Assert.Contains("BuildGitSettingsDialog", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ShowDialog(owner)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("new GitHubIntegrationControl(database)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<MainForm", bootstrap, StringComparison.Ordinal);

        Assert.DoesNotContain("TryInjectIntoOpenSettingsForms", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(SettingsForm)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.TabPages.Add", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedGitSettingsLoadsOnlyWhenItsDialogIsShown()
    {
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("dialog.Shown += async", bootstrap, StringComparison.Ordinal);
        Assert.Contains("await control.LoadAsync()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("GitHubIntegrationUiBootstrap.LoadDedicatedDialog", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
