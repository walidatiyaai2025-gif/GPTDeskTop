using System.Diagnostics;
using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyLiveManagedChromeFieldRegressionTests
{
    private const string PhysicalGateVariable = "GPTDESKTOP_RUN_PHYSICAL_LIVE_CHROME";

    [Fact]
    public async Task HealthySavedManagedChromeSurvivesAppStartupAndIdleGuardPolling()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(PhysicalGateVariable), "1", StringComparison.Ordinal))
            return;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The live managed Chrome field-regression gate requires Windows.");

        var chromeExe = FindChromeExecutable()
            ?? throw new InvalidOperationException("Google Chrome is required for the physical live-session preservation gate.");
        var profile = ChromeProfileCatalog.Discover().First();
        await using var session = new SimpleMonitorProfileSession(profile);

        Directory.CreateDirectory(profile.ManagedUserDataDirectory);
        KillChromeForManagedDirectory(profile.ManagedUserDataDirectory);

        var appExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "bin", "Release", "net8.0-windows", "GPTDeskTop.exe"));
        if (!File.Exists(appExe))
            throw new FileNotFoundException("Build the Monitor Only application before running the physical live-session gate.", appExe);

        var databasePath = Path.Combine(Path.GetDirectoryName(appExe)!, "appdata.db");
        var database = new LocalDatabase(databasePath);
        await database.InitializeAsync();
        await database.SetSettingAsync("SimpleMonitor.ProfileKey", profile.Key);

        Process? app = null;
        try
        {
            var chromeArgs = $"--remote-debugging-port={session.DebuggingPort} --user-data-dir=\"{profile.ManagedUserDataDirectory}\" --new-window about:blank";
            _ = Process.Start(new ProcessStartInfo(chromeExe, chromeArgs) { UseShellExecute = true });

            await WaitForEndpointAsync(session.DebuggingPort, expectedAlive: true, TimeSpan.FromSeconds(12));

            app = Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true })
                ?? throw new InvalidOperationException("GPTDeskTop could not be started for the physical live-session gate.");

            // Cover startup reconciliation plus several 750ms lifetime-guard polling cycles.
            await Task.Delay(TimeSpan.FromSeconds(6));

            if (app.HasExited)
                throw new InvalidOperationException($"GPTDeskTop exited during healthy managed Chrome adoption. ExitCode={app.ExitCode}");

            await WaitForEndpointAsync(session.DebuggingPort, expectedAlive: true, TimeSpan.FromSeconds(3));
            Assert.True(await session.IsAutomationSessionAvailableAsync(),
                "The already-working selected GPTDeskTop-managed Chrome was closed or detached during app startup/idle enforcement.");
        }
        finally
        {
            if (app is { HasExited: false })
            {
                try { app.Kill(entireProcessTree: true); } catch { }
                try { app.WaitForExit(3000); } catch { }
            }

            KillChromeForManagedDirectory(profile.ManagedUserDataDirectory);
        }
    }

    private static string? FindChromeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task WaitForEndpointAsync(int port, bool expectedAlive, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var alive = false;
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/json/version");
                alive = response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }

            if (alive == expectedAlive) return;
            await Task.Delay(200);
        }

        throw new InvalidOperationException($"CDP {port} did not reach expectedAlive={expectedAlive} within {timeout}.");
    }

    private static void KillChromeForManagedDirectory(string managedDirectory)
    {
        // This physical test runs in an isolated GitHub Actions VM. The dedicated job launches no
        // ordinary Chrome before this test, so cleaning Chrome processes here is isolated to the job.
        foreach (var process in Process.GetProcessesByName("chrome"))
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
