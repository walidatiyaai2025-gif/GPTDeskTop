using System.Diagnostics;
using System.Management;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Process-level hard gate for Monitor Only. The guard blocks delayed or competing GPTDeskTop-owned
/// Chrome starts while preserving one verified healthy managed browser for the saved selected profile.
/// Ordinary user Chrome is never a candidate.
/// </summary>
internal static class MonitorOnlyManagedChromeGuard
{
    private const string ProfileSourceMarker = "gptdesktop-profile-source.txt";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ProcessStartSettleDelay = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan ExplicitStartFreshness = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly object Sync = new();

    private static CancellationTokenSource? _cancellation;
    private static Task? _poller;
    private static ManagementEventWatcher? _watcher;
    private static string? _authorizedDirectory;
    private static DateTime _appStartUtc;
    private static int _enforcing;

    internal static void Start(ChromeProfileInfo? savedSelectedProfile)
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (Sync)
        {
            if (_cancellation is not null) return;

            _appStartUtc = SafeCurrentProcessStartUtc();
            _authorizedDirectory = TryResolveHealthySavedSelection(savedSelectedProfile);
            _cancellation = new CancellationTokenSource();
            TryStartProcessWatcher();
            _poller = Task.Run(() => PollLoopAsync(_cancellation.Token));
        }

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

    private static string? TryResolveHealthySavedSelection(ChromeProfileInfo? profile)
    {
        if (profile is null) return null;

        var selectedDirectory = NormalizeDirectory(profile.ManagedUserDataDirectory);
        IReadOnlyList<ManagedChromeProcess> processes;
        try
        {
            processes = DiscoverManagedChromeProcesses();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            TryLogException(ex, "MonitorOnlyManagedChromeGuard.AdoptSavedSelection");
            return null;
        }

        if (!processes.Any(process => PathsEqual(process.UserDataDirectory, selectedDirectory)))
            return null;

        var port = SimpleMonitorChromeOwnershipGate.ResolveStablePort(profile.Key);
        if (!IsEndpointAlive(port)) return null;

        TryRecord(
            "IdleChromeGuard",
            "HealthySavedSessionAdopted",
            $"cdp={port}",
            selectedDirectory);
        return selectedDirectory;
    }

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
        if (IsAuthorizedDirectory(managed.UserDataDirectory)) return true;

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

        // A fresh marker is written only by the explicit Start Monitor launch path immediately
        // before Process.Start. It is therefore safe to transfer authorization from an adopted
        // saved profile to a newly selected profile when the operator explicitly changes profiles.
        lock (Sync)
            _authorizedDirectory = NormalizeDirectory(managed.UserDataDirectory);

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

    private static bool IsEndpointAlive(int port)
    {
        using var client = new HttpClient { Timeout = ProbeTimeout };
        try
        {
            using var response = client.GetAsync($"http://127.0.0.1:{port}/json/version").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return false;
        }
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
        }
        catch (InvalidOperationException)
        {
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
