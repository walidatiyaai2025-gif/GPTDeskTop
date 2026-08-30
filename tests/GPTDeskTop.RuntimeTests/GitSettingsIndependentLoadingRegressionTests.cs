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
        Assert.Contains("VisibleChanged += async", bootstrap, StringComparison.Ordinal);
        Assert.Contains("if (Visible) await EnsureLoadedAsync()", bootstrap, StringComparison.Ordinal);
        Assert.Contains("await _control.LoadAsync()", bootstrap, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
