using System.Diagnostics;
using System.Management;

namespace GPTDeskTop.Services;

/// <summary>
/// Process-level hard gate for the Monitor Only product.
///
/// The v2.0.42 cold-start reconciliation was intentionally one-shot. That left a race where a
/// delayed legacy/background component could start a GPTDeskTop-owned Chrome after the idle UI had
/// already appeared. This guard starts before the Monitor Only UI, watches Chrome process creation
/// and also polls as a fallback. While no explicit Start Monitor launch has been observed, every
/// Chrome whose command line points at a GPTDeskTop-owned user-data directory is terminated.
/// Ordinary user Chrome is never a candidate.
///
/// The one legal launch path (SimpleMonitorProfileSession) writes gptdesktop-profile-source.txt
/// immediately before Process.Start. A fresh marker created by this app instance is therefore the
/// authorization handshake. The guard latches that exact managed directory and never authorizes a
/// different GPTDeskTop directory during the process lifetime.
/// </summary>
internal static class MonitorOnlyManagedChromeGuard
{
    private const string ProfileSourceMarker = "gptdesktop-profile-source.txt";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ProcessStartSettleDelay = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan ExplicitStartFreshness = TimeSpan.FromSeconds(10);
    private static readonly object Sync = new();

    private static CancellationTokenSource? _cancellation;
    private static Task? _poller;
    private static ManagementEventWatcher? _watcher;
    private static string? _authorizedDirectory;
    private static DateTime _appStartUtc;
    private static int _enforcing;

    internal static void Start()
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (Sync)
        {
            if (_cancellation is not null) return;

            _appStartUtc = SafeCurrentProcessStartUtc();
            _authorizedDirectory = null;
            _cancellation = new CancellationTokenSource();
            TryStartProcessWatcher();
            _poller = Task.Run(() => PollLoopAsync(_cancellation.Token));
        }

        // Do not wait for the UI. A managed Chrome left behind by an earlier run must be removed
        // before Monitor Only can become idle and visible.
        EnforceNow("startup");
    }

    internal static bool HasExplicitStartAuthorization(string managedUserDataDirectory)
    {
        var normalized = NormalizeDirectory(managedUserDataDirectory);
        lock (Sync)
            return _authorizedDirectory is not null
                && PathsEqual(_authorizedDirectory, normalized);
    }

    internal static void RevokeExplicitStartAuthorization()
    {
        lock (Sync) _authorizedDirectory = null;
        EnforceNow("authorization-revoked");
    }

    internal static void EnforceIdleNowForTests()
        => EnforceNow("test");

    private static void TryStartProcessWatcher()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='chrome.exe'"));
            _watcher.EventArrived += OnChromeProcessStarted;
            _watcher.Start();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Polling remains the independent fallback. Do not make startup depend on WMI event
            // subscriptions; the existing cold-start reconciler still performs the synchronous
            // fail-closed inventory before the UI is created.
            TryLogException(ex, "MonitorOnlyManagedChromeGuard.StartWatcher");
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
        }
    }

    private static void OnChromeProcessStarted(object sender, EventArrivedEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Win32_ProcessStartTrace can arrive before CommandLine is queryable. Give Windows
                // a few milliseconds, then inspect ownership and terminate only GPTDeskTop roots.
                await Task.Delay(ProcessStartSettleDelay).ConfigureAwait(false);
                EnforceNow("process-start");
            }
            catch (Exception ex)
            {
                TryLogException(ex, "MonitorOnlyManagedChromeGuard.ProcessStart");
            }
        });
    }

    private static async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            EnforceNow("poll");
            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static void EnforceNow(string reason)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Interlocked.Exchange(ref _enforcing, 1) != 0) return;

        try
        {
            foreach (var managed in DiscoverManagedChromeProcesses())
            {
                if (IsAuthorizedDirectory(managed.UserDataDirectory)) continue;
                if (TryLatchExplicitStartAuthorization(managed)) continue;

                TryRecord(
                    "IdleChromeGuard",
                    "BlockedManagedChrome",
                    $"pid={managed.ProcessId}; reason={reason}",
                    managed.UserDataDirectory);
                KillManagedProcessTree(managed.ProcessId);
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            TryLogException(ex, "MonitorOnlyManagedChromeGuard.Enforce");
        }
        finally
        {
            Volatile.Write(ref _enforcing, 0);
        }
    }

    private static bool TryLatchExplicitStartAuthorization(ManagedChromeProcess managed)
    {
        // A previously latched exact directory remains the only authorized Monitor Chrome root.
        if (IsAuthorizedDirectory(managed.UserDataDirectory)) return true;
        lock (Sync)
        {
            if (_authorizedDirectory is not null) return false;
        }

        var marker = Path.Combine(managed.UserDataDirectory, ProfileSourceMarker);
        if (!File.Exists(marker)) return false;

        DateTime markerUtc;
        try { markerUtc = File.GetLastWriteTimeUtc(marker); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        var processStartUtc = SafeProcessStartUtc(managed.ProcessId);
        if (processStartUtc == DateTime.MinValue) return false;

        var nowUtc = DateTime.UtcNow;
        if (markerUtc < _appStartUtc - TimeSpan.FromSeconds(1)) return false;
        if (nowUtc - markerUtc > ExplicitStartFreshness) return false;
        if (processStartUtc + TimeSpan.FromSeconds(1) < markerUtc) return false;
        if (processStartUtc - markerUtc > ExplicitStartFreshness) return false;

        lock (Sync)
        {
            if (_authorizedDirectory is not null)
                return PathsEqual(_authorizedDirectory, managed.UserDataDirectory);
            _authorizedDirectory = NormalizeDirectory(managed.UserDataDirectory);
        }

        TryRecord(
            "IdleChromeGuard",
            "ExplicitStartAuthorized",
            $"pid={managed.ProcessId}",
            managed.UserDataDirectory);
        return true;
    }

    private static bool IsAuthorizedDirectory(string directory)
    {
        lock (Sync)
            return _authorizedDirectory is not null && PathsEqual(_authorizedDirectory, directory);
    }

    private static IReadOnlyList<ManagedChromeProcess> DiscoverManagedChromeProcesses()
    {
        var managedDirectories = ChromeProfileCatalog.Discover()
            .Select(profile => NormalizeDirectory(profile.ManagedUserDataDirectory))
            .Append(NormalizeDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GPTDeskTop",
                "ChromeProfile")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<ManagedChromeProcess>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='chrome.exe'");
        using var collection = searcher.Get();
        foreach (ManagementObject row in collection)
        {
            var commandLine = row["CommandLine"] as string;
            if (string.IsNullOrWhiteSpace(commandLine)) continue;

            var directory = managedDirectories.FirstOrDefault(candidate =>
                CommandLineReferencesUserDataDirectory(commandLine, candidate));
            if (directory is null) continue;

            var processId = Convert.ToInt32(row["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
            if (processId <= 0) continue;
            results.Add(new ManagedChromeProcess(processId, directory));
        }

        return results;
    }

    private static bool CommandLineReferencesUserDataDirectory(string commandLine, string directory)
    {
        var quoted = $"--user-data-dir=\"{directory}\"";
        var unquoted = $"--user-data-dir={directory}";
        return commandLine.Contains(quoted, StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains(unquoted, StringComparison.OrdinalIgnoreCase);
    }

    private static void KillManagedProcessTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(2000);
        }
        catch (ArgumentException)
        {
            // Process already exited between WMI inventory and ownership enforcement.
        }
        catch (InvalidOperationException)
        {
            // Process already exited or no longer has a valid handle.
        }
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

    private static DateTime SafeCurrentProcessStartUtc()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    private static DateTime SafeProcessStartUtc(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void TryRecord(string area, string action, string detail, string context)
    {
        try { RuntimeFlightRecorder.Record(area, action, detail, context); }
        catch { }
    }

    private static void TryLogException(Exception exception, string area)
    {
        try { ExceptionLogService.Log(exception, area); }
        catch { }
    }

    private sealed record ManagedChromeProcess(int ProcessId, string UserDataDirectory);
}
