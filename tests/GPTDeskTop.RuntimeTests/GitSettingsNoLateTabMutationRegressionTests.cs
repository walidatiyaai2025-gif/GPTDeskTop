namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsNoLateTabMutationRegressionTests
{
    [Fact]
    public void GitHubBootstrapNeverMutatesApplicationSettingsTabs()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.DoesNotContain("TabPage(\"GitHub\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TabPages.Add", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetField(\"_tabs\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryInjectIntoOpenSettingsForms", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
