namespace GPTDeskTop.RuntimeTests;

public sealed class LastReleasePublisherRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void PublisherRequiresCompleteStableGateSetForSameMainCommit()
    {
        var workflow = ReadSource(".github", "workflows", "update-last-release.yml");

        Assert.Contains("workflows: [\"Build GPTDeskTop\"]", workflow, StringComparison.Ordinal);
        Assert.Contains("branches: [main]", workflow, StringComparison.Ordinal);
        Assert.Contains("'Build GPTDeskTop'", workflow, StringComparison.Ordinal);
        Assert.Contains("'QA Release x64'", workflow, StringComparison.Ordinal);
        Assert.Contains("'QA Hidden Chrome CDP'", workflow, StringComparison.Ordinal);
        Assert.Contains("'QA Crash Process Recovery'", workflow, StringComparison.Ordinal);
        Assert.Contains("'QA Passive Chat Wait'", workflow, StringComparison.Ordinal);
        Assert.Contains("'Development Delivery Receipts'", workflow, StringComparison.Ordinal);
        Assert.Contains("'Development Task Recovery'", workflow, StringComparison.Ordinal);
        Assert.Contains("'Development Message Reload'", workflow, StringComparison.Ordinal);
        Assert.Contains("head_sha=$env:TARGET_SHA", workflow, StringComparison.Ordinal);
        Assert.Contains("$run.conclusion -ne 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("group: update-last-release-main", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherProducesCompressedSingleFileWindowsX64ExeAndGuardsMainFromStaleOverwrite()
    {
        var workflow = ReadSource(".github", "workflows", "update-last-release.yml");

        Assert.Contains("--runtime win-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:EnableCompressionInSingleFile=true", workflow, StringComparison.Ordinal);
        Assert.Contains("Last release/GPTDeskTop.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Last release/RELEASE.txt", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash 'Last release/GPTDeskTop.exe' -Algorithm SHA256", workflow, StringComparison.Ordinal);
        Assert.Contains("if ($remoteMain -ne $target)", workflow, StringComparison.Ordinal);
        Assert.Contains("95MB", workflow, StringComparison.Ordinal);
        Assert.Contains("[skip ci]", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherStampsAndVerifiesStableBuildIdentity()
    {
        var workflow = ReadSource(".github", "workflows", "update-last-release.yml");

        Assert.Contains("$buildId = $source.Substring(0, 8).ToLowerInvariant()", workflow, StringComparison.Ordinal);
        Assert.Contains("$informationalVersion = \"$productVersion+stable.$buildId\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$informationalVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("VersionInfo.ProductVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("Stable build ID: $buildId", workflow, StringComparison.Ordinal);
        Assert.Contains("Informational version: $embeddedProductVersion", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramShowsStableBuildIdentityOnlyForStampedBuilds()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("ApplicationBuildIdentity.StableBuildId is not null", program, StringComparison.Ordinal);
        Assert.Contains("mainForm.Text = $\"GPTDeskTop {ApplicationBuildIdentity.DisplayVersion}\";", program, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryContainsOperatorVisibleLastReleaseContract()
    {
        var readme = ReadSource("Last release", "README.md");
        Assert.Contains("GPTDeskTop.exe", readme, StringComparison.Ordinal);
        Assert.Contains("latest verified Windows x64 Release application", readme, StringComparison.Ordinal);
        Assert.Contains("eight required stable CI workflows", readme, StringComparison.Ordinal);
        Assert.Contains("stable build ID", readme, StringComparison.OrdinalIgnoreCase);
    }
}
