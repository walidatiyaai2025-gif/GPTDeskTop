namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsIndependentLoadingRegressionTests
{
    [Fact]
    public void ApplicationSettingsLoadPathHasNoGitHubControlLoad()
    {
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var bootstrap = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.DoesNotContain("GitHubIntegrationControl", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHubApiProbeService", settings, StringComparison.Ordinal);
        Assert.Contains("dialog.Shown += async", bootstrap, StringComparison.Ordinal);
        Assert.Contains("await control.LoadAsync()", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
