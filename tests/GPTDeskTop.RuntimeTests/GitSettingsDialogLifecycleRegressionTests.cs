namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsDialogLifecycleRegressionTests
{
    [Fact]
    public void EmbeddedGitSettingsUsesSingleLazyDestinationInsteadOfDisposableDialog()
    {
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.Contains("CreateEmbeddedGitSettingsSurface", bootstrap, StringComparison.Ordinal);
        Assert.Contains("new EmbeddedGitSettingsSurface(database)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, GitSettingsDestination", shell, StringComparison.Ordinal);
        Assert.Contains("GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main)", shell, StringComparison.Ordinal);

        Assert.DoesNotContain("BuildGitSettingsDialog", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog(owner)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("static Form? GitSettings", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("static Form GitSettings", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly Form", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
