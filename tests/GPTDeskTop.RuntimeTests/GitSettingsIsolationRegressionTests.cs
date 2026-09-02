namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsIsolationRegressionTests
{
    [Fact]
    public void GitHubSettingsAreIsolatedFromApplicationSettingsControlTree()
    {
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.Contains("Git Settings", bootstrap, StringComparison.Ordinal);
        Assert.Contains("PremiumRuntimeShellExperience.NavigateTo(main, \"GitHub / Git Settings\")", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CreateEmbeddedGitSettingsSurface", bootstrap, StringComparison.Ordinal);
        Assert.Contains("new GitHubIntegrationControl(database)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<MainForm", bootstrap, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, GitSettingsDestination", shell, StringComparison.Ordinal);

        Assert.DoesNotContain("TryInjectIntoOpenSettingsForms", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(SettingsForm)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.TabPages.Add", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("new GitHubIntegrationControl", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedGitSettingsLoadsOnlyWhenItsDestinationBecomesVisible()
    {
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("VisibleChanged += async", bootstrap, StringComparison.Ordinal);
        Assert.Contains("if (Visible) await EnsureLoadedAsync();", bootstrap, StringComparison.Ordinal);
        Assert.Contains("await _control.LoadAsync();", bootstrap, StringComparison.Ordinal);
        Assert.Contains("GitHubIntegrationUiBootstrap.LoadEmbeddedWorkspace", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
