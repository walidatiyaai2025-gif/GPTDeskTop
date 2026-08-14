namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsBranchBootstrapRegressionTests
{
    [Fact]
    public void PerRepositoryBranchEnhancementTargetsAnyGitHubControlHost()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubPerRepositoryBranchUiBootstrap.cs");

        Assert.Contains("Application.OpenForms", source, StringComparison.Ordinal);
        Assert.Contains("OfType<GitHubIntegrationControl>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsForm", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
