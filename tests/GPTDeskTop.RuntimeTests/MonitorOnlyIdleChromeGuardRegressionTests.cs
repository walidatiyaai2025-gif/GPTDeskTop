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
    public void IdleGuardStartsBeforeUiWithSavedProfileAndWatchesDelayedChromeStarts()
    {
        var startup = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");

        var savedProfile = startup.IndexOf("ResolveSavedSelectedProfile(database)", StringComparison.Ordinal);
        var guardStart = startup.IndexOf("MonitorOnlyManagedChromeGuard.Start(savedSelectedProfile)", StringComparison.Ordinal);
        var coldCleanup = startup.IndexOf("MonitorOnlyColdStartChromeReconciler.ReconcileBeforeIdleUi(savedSelectedProfile)", StringComparison.Ordinal);
        var form = startup.IndexOf("new SimpleMonitorForm(database)", StringComparison.Ordinal);
        Assert.True(savedProfile >= 0 && guardStart > savedProfile && coldCleanup > guardStart && form > coldCleanup,
            "The saved profile must seed the lifetime guard before cold-start reconciliation and before the Monitor Only UI exists.");

        Assert.Contains("Win32_ProcessStartTrace", guard, StringComparison.Ordinal);
        Assert.Contains("ProcessName='chrome.exe'", guard, StringComparison.Ordinal);
        Assert.Contains("PollLoopAsync", guard, StringComparison.Ordinal);
        Assert.Contains("EnforceNow(\"startup\")", guard, StringComparison.Ordinal);
        Assert.Contains("EnforceNow(\"process-start\")", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthySavedManagedChromeIsAdoptedBeforeIdleEnforcement()
    {
        var startup = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");
        var cleanup = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyColdStartChromeReconciler.cs");

        Assert.Contains("SimpleMonitor.ProfileKey", startup, StringComparison.Ordinal);
        Assert.Contains("TryResolveHealthySavedSelection", guard, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorChromeOwnershipGate.ResolveStablePort(profile.Key)", guard, StringComparison.Ordinal);
        Assert.Contains("IsEndpointAlive(port)", guard, StringComparison.Ordinal);
        Assert.Contains("HealthySavedSessionAdopted", guard, StringComparison.Ordinal);

        Assert.Contains("selectedEndpointAlive", cleanup, StringComparison.Ordinal);
        Assert.Contains("selectedProcessExists", cleanup, StringComparison.Ordinal);
        Assert.Contains("preserveSelected", cleanup, StringComparison.Ordinal);
        Assert.Contains("PathsEqual(managed.UserDataDirectory, selectedDirectory)", cleanup, StringComparison.Ordinal);
        Assert.Contains("continue;", cleanup, StringComparison.Ordinal);
        Assert.Contains("disappeared during cold-start adoption", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void ColdStartStillRemovesStaleOrCompetingManagedChrome()
    {
        var cleanup = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyColdStartChromeReconciler.cs");

        Assert.Contains("KillManagedProcessTree(managed)", cleanup, StringComparison.Ordinal);
        Assert.Contains("invalidSurvivors", cleanup, StringComparison.Ordinal);
        Assert.Contains("Competing GPTDeskTop-managed Chrome", cleanup, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleGuardTargetsOnlyGptDesktopManagedUserDataDirectories()
    {
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("ChromeProfileCatalog.Discover()", guard, StringComparison.Ordinal);
        Assert.Contains("\"GPTDeskTop\",\n                \"ChromeProfile\"", guard, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", guard, StringComparison.Ordinal);
        Assert.Contains("CommandLineReferencesUserDataDirectory", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", guard, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshExplicitStartCanTransferAuthorizationToNewlySelectedProfile()
    {
        var guard = ReadSource("src", "GPTDeskTop", "Services", "MonitorOnlyManagedChromeGuard.cs");
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.Contains("gptdesktop-profile-source.txt", guard, StringComparison.Ordinal);
        Assert.Contains("ExplicitStartFreshness", guard, StringComparison.Ordinal);
        Assert.Contains("markerUtc < _appStartUtc", guard, StringComparison.Ordinal);
        Assert.Contains("_authorizedDirectory = NormalizeDirectory(managed.UserDataDirectory)", guard, StringComparison.Ordinal);
        Assert.Contains("transfer authorization", guard, StringComparison.OrdinalIgnoreCase);

        var markerWrite = session.IndexOf("File.WriteAllText(", StringComparison.Ordinal);
        var processStart = session.IndexOf("Process.Start(new ProcessStartInfo", StringComparison.Ordinal);
        Assert.True(markerWrite >= 0 && processStart > markerWrite,
            "The explicit Start Monitor marker must be written immediately before the one legal Chrome Process.Start boundary.");
    }
}
