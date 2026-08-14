namespace GPTDeskTop.RuntimeTests;

public sealed class GitSettingsLoadFailureRegressionTests
{
    [Fact]
    public void GitHubLoadFailureIsContainedInsideDedicatedDialog()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "GitHubIntegrationUiBootstrap.cs");

        Assert.Contains("try", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("GitHub settings could not be loaded", source, StringComparison.Ordinal);
        Assert.Contains("MessageBox.Show", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}
