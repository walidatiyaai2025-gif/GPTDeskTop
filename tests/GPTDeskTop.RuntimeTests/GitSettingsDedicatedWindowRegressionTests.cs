namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsDedicatedWindowRegressionTests
{
    [Fact]
    public void GitSettingsHasStableEmbeddedPremiumHost()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");
        var shell = ReadSource("src", "GPTDeskTop", "UI", "PremiumRuntimeShellExperience.cs");

        Assert.Contains("Name = \"PremiumGitSettingsWorkspace\"", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"GitHub and Git Settings workspace\"", source, StringComparison.Ordinal);
        Assert.Contains("host.Controls.Add(_control)", source, StringComparison.Ordinal);
        Assert.Contains("GitHubIntegrationUiBootstrap.CreateEmbeddedGitSettingsSurface(main)", shell, StringComparison.Ordinal);
        Assert.Contains("Name = \"PremiumContentHost\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog(owner)", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
