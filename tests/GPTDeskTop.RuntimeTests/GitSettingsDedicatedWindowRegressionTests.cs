namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsDedicatedWindowRegressionTests
{
    [Fact]
    public void GitSettingsWindowHasStableStandaloneHost()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("GPTDeskTop — Git Settings", source, StringComparison.Ordinal);
        Assert.Contains("FormBorderStyle.Sizable", source, StringComparison.Ordinal);
        Assert.Contains("StartPosition = FormStartPosition.CenterParent", source, StringComparison.Ordinal);
        Assert.Contains("host.Controls.Add(new GitHubIntegrationControl(database)", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
