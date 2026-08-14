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

        // A static factory method is safe; what must never return is a cached/static dialog instance.
        Assert.DoesNotContain("static Form? GitSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static Form GitSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly Form", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
