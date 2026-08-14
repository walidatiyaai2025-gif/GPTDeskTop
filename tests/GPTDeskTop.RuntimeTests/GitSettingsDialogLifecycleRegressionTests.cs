namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsDialogLifecycleRegressionTests
{
    [Fact]
    public void DedicatedGitSettingsCreatesFreshDisposableDialogPerOpen()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("using var dialog = BuildGitSettingsDialog(database)", source, StringComparison.Ordinal);
        Assert.Contains("new GitHubIntegrationControl(database)", source, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog(owner)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static Form", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
