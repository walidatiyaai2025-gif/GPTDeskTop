namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsMenuContractRegressionTests
{
    [Fact]
    public void GitSettingsCommandIsInstalledOnceAndDoesNotDependOnApplicationSettings()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("InstallationState", source, StringComparison.Ordinal);
        Assert.Contains("state.Installed", source, StringComparison.Ordinal);
        Assert.Contains("FindApplicationMenu", source, StringComparison.Ordinal);
        Assert.Contains("ToolStripMenuItem(\"Git Settings\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HashSet<nint> Injected", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
