namespace GPTDeskTop.RuntimeTests;

public sealed class InstallerSelfCopyRegressionTests
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
    public void InstallerUsesDedicatedUninstallerCopyInsteadOfOverwritingSetupExecutable()
    {
        var source = ReadSource("src", "GPTDeskTop.Setup", "Program.cs");

        Assert.Contains("Path.Combine(installDir, \"GPTDeskTop-Uninstall.exe\")", source, StringComparison.Ordinal);
        Assert.Contains("if (!string.Equals(setupSource, Path.GetFullPath(setupCopy), StringComparison.OrdinalIgnoreCase))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var setupCopy = Path.Combine(installDir, \"GPTDeskTop-Setup.exe\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySetupCleanupCannotAbortInstallationOrUninstall()
    {
        var source = ReadSource("src", "GPTDeskTop.Setup", "Program.cs");

        Assert.Contains("TryDelete(Path.Combine(installDir, \"GPTDeskTop-Setup.exe\"));", source, StringComparison.Ordinal);
        Assert.Contains("try { if (File.Exists(path)) File.Delete(path); } catch { }", source, StringComparison.Ordinal);
        Assert.Contains("CreateShortcut(Path.Combine(startMenuDir, \"Uninstall GPTDeskTop.lnk\"), setupCopy, installDir, \"/uninstall\")", source, StringComparison.Ordinal);
        Assert.Contains("RegisterUninstall(setupCopy, installDir)", source, StringComparison.Ordinal);
    }
}