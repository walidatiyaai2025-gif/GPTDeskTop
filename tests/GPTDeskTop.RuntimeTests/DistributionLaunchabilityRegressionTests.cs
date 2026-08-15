namespace GPTDeskTop.RuntimeTests;

public sealed class DistributionLaunchabilityRegressionTests
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
    public void PeValidatorRequiresMzPeAmd64AndPe32Plus()
    {
        var source = ReadSource("scripts", "Test-WindowsX64Pe.ps1");

        Assert.Contains("0x4D", source, StringComparison.Ordinal);
        Assert.Contains("0x5A", source, StringComparison.Ordinal);
        Assert.Contains("0x3C", source, StringComparison.Ordinal);
        Assert.Contains("0x50", source, StringComparison.Ordinal);
        Assert.Contains("0x45", source, StringComparison.Ordinal);
        Assert.Contains("0x8664", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x020B", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AMD64/x64", source, StringComparison.Ordinal);
        Assert.Contains("PE32+ / 64-bit", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupVerifierRejectsMalformedOrWrongArchitectureApplicationPayload()
    {
        var source = ReadSource("src", "GPTDeskTop.Setup", "Program.cs");

        Assert.Contains("VerifyWindowsX64Pe(executable, out error)", source, StringComparison.Ordinal);
        Assert.Contains("reader.ReadUInt16() != 0x5A4D", source, StringComparison.Ordinal);
        Assert.Contains("reader.ReadUInt32() != 0x00004550", source, StringComparison.Ordinal);
        Assert.Contains("reader.ReadUInt16() != 0x8664", source, StringComparison.Ordinal);
        Assert.Contains("reader.ReadUInt16() != 0x020B", source, StringComparison.Ordinal);
        Assert.Contains("--verify-payload", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubReleaseRequiresExactPublishedAppToPassPeAndManagedStartupProbe()
    {
        var workflow = ReadSource(".github", "workflows", "release-artifact.yml");

        Assert.Contains("Output/Publish/GPTDeskTop.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-WindowsX64Pe.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("--qa-crash-probe-verify", workflow, StringComparison.Ordinal);
        Assert.Contains("managed-startup probe receipt", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$probe.ExitCode -ne 0", workflow, StringComparison.Ordinal);
        Assert.Contains("PE32+/AMD64 validation", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseX64QaRequiresPublishedAppToLoadAndExecuteManagedStartup()
    {
        var workflow = ReadSource(".github", "workflows", "qa-release-x64.yml");

        Assert.Contains("Output/Publish/GPTDeskTop.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-WindowsX64Pe.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("--qa-crash-probe-verify", workflow, StringComparison.Ordinal);
        Assert.Contains("failed process launch probe", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verify embedded setup payload", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-payload", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LastReleaseCannotBePublishedWithoutPeAndRealProcessProbe()
    {
        var workflow = ReadSource(".github", "workflows", "update-last-release.yml");

        Assert.Contains("Test-WindowsX64Pe.ps1 -Path $exe", workflow, StringComparison.Ordinal);
        Assert.Contains("--qa-crash-probe-verify", workflow, StringComparison.Ordinal);
        Assert.Contains("failed Windows process-load/managed-startup probe", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-WindowsX64Pe.ps1 -Path 'Last release/GPTDeskTop.exe'", workflow, StringComparison.Ordinal);
        Assert.Contains("PE32+/AMD64 + real Windows process-load/managed-startup probe passed", workflow, StringComparison.Ordinal);
    }
}
