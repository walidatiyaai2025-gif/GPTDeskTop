namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyIdleChromeGuardRegressionTests
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
    public void IdleGuardStartsBeforeProgramAndWatchesDelayedChromeStarts()
    {
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");

        Assert.Contains("[ModuleInitializer]", guard, StringComparison.Ordinal);
        Assert.Contains("Win32_ProcessStartTrace", guard, StringComparison.Ordinal);
        Assert.Contains("ProcessName='chrome.exe'", guard, StringComparison.Ordinal);
        Assert.Contains("PollLoopAsync", guard, StringComparison.Ordinal);
        Assert.Contains("EnforceNow(\"module-init\")", guard, StringComparison.Ordinal);
        Assert.Contains("EnforceNow(\"process-start\")", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleGuardTargetsOnlyGptDesktopManagedUserDataDirectories()
    {
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");

        Assert.Contains("ChromeProfileCatalog.Discover()", guard, StringComparison.Ordinal);
        Assert.Contains("\"GPTDeskTop\",\n                \"ChromeProfile\"", guard, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", guard, StringComparison.Ordinal);
        Assert.Contains("CommandLineReferencesUserDataDirectory", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", guard, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyFreshExplicitStartMarkerCanAuthorizeManagedChrome()
    {
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.Contains("gptdesktop-profile-source.txt", guard, StringComparison.Ordinal);
        Assert.Contains("ExplicitStartFreshness", guard, StringComparison.Ordinal);
        Assert.Contains("markerUtc < _appStartUtc", guard, StringComparison.Ordinal);
        Assert.Contains("_authorizedDirectory", guard, StringComparison.Ordinal);

        var markerWrite = session.IndexOf("File.WriteAllText(", StringComparison.Ordinal);
        var processStart = session.IndexOf("Process.Start(new ProcessStartInfo", StringComparison.Ordinal);
        Assert.True(markerWrite >= 0 && processStart > markerWrite,
            "The explicit Start Monitor marker must be written immediately before the one legal Chrome Process.Start boundary.");
    }
}
