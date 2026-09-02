namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsDialogLifecycleRegressionTests
{
    [Fact]
    public void GitSettingsUsesEmbeddedDisposableControlInsteadOfPersistentDialog()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.Contains("private sealed class EmbeddedGitSettingsSurface : UserControl", source, StringComparison.Ordinal);
        Assert.Contains("new GitHubIntegrationControl(database)", source, StringComparison.Ordinal);
        Assert.Contains("CreateEmbeddedGitSettingsSurface", source, StringComparison.Ordinal);
        Assert.Contains("GetOrCreate(registration, GitSettingsDestination", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog(owner)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly Form", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
