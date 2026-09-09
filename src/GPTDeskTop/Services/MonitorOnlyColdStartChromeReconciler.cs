using System.Diagnostics;
using System.Management;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Reconciles GPTDeskTop-owned Chrome processes before the Monitor Only idle UI is shown.
/// A healthy managed browser for the saved selected profile is adopted and preserved; only stale
/// or competing GPTDeskTop-managed process trees are removed. Ordinary user Chrome is never targeted.
/// </summary>
internal static class MonitorOnlyColdStartChromeReconciler
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);

    internal static void ReconcileBeforeIdleUi(ChromeProfileInfo? selectedProfile)
    {
        if (!OperatingSystem.IsWindows()) return;

        IReadOnlyList<ManagedChromeProcess> processes;
        try
        {
            processes = DiscoverManagedChromeProcesses();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "GPTDeskTop could not verify the managed Chrome process inventory during cold start. Monitor Only startup is blocked rather than leave an unverified managed Chrome running.",
                ex);
        }

        var selectedDirectory = selectedProfile is null ? null : NormalizeDirectory(selectedProfile.ManagedUserDataDirectory);
        var selectedPort = selectedProfile is null ? (int?)null : SimpleMonitorChromeOwnershipGate.ResolveStablePort(selectedProfile.Key);
        var selectedEndpointAlive = selectedPort is not null && IsEndpointAlive(selectedPort.Value);
        var selectedProcessExists = selectedDirectory is not null
            && processes.Any(process => PathsEqual(process.UserDataDirectory, selectedDirectory));
        var preserveSelected = selectedEndpointAlive && selectedProcessExists;

        foreach (var managed in processes.OrderBy(process => process.ProcessId))
        {
            if (preserveSelected
                && selectedDirectory is not null
                && PathsEqual(managed.UserDataDirectory, selectedDirectory))
            {
                continue;
            }

            KillManagedProcessTree(managed);
        }

        IReadOnlyList<ManagedChromeProcess> survivors;
        try
        {
            survivors = DiscoverManagedChromeProcesses();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "GPTDeskTop could not verify managed Chrome cleanup after cold start reconciliation. Monitor Only startup is blocked.",
                ex);
        }

        if (preserveSelected && selectedDirectory is not null && selectedPort is not null)
        {
            var invalidSurvivors = survivors
                .Where(process => !PathsEqual(process.UserDataDirectory, selectedDirectory))
                .ToArray();
            if (invalidSurvivors.Length > 0)
            {
                var ids = string.Join(", ", invalidSurvivors.Select(process => process.ProcessId));
                throw new InvalidOperationException(
                    $"Competing GPTDeskTop-managed Chrome is still running after cold start cleanup (PID(s): {ids}). Monitor Only startup is blocked.");
            }

            if (survivors.Count == 0 || !IsEndpointAlive(selectedPort.Value))
            {
                throw new InvalidOperationException(
                    $"The selected GPTDeskTop-managed Chrome on CDP {selectedPort.Value} disappeared during cold-start adoption. Monitor Only startup is blocked rather than open another browser.");
            }

            return;
        }

        if (survivors.Count > 0)
        {
            var ids = string.Join(", ", survivors.Select(process => process.ProcessId));
            throw new InvalidOperationException(
                $"GPTDeskTop-managed Chrome is still running after cold start cleanup (PID(s): {ids}). Monitor Only startup is blocked until those managed processes exit.");
        }
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
            if (processId > 0) results.Add(new ManagedChromeProcess(processId, directory));
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

    private static void KillManagedProcessTree(ManagedChromeProcess managed)
    {
        try
        {
            using var process = Process.GetProcessById(managed.ProcessId);
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit((int)CloseTimeout.TotalMilliseconds))
            {
                throw new InvalidOperationException(
                    $"GPTDeskTop-managed Chrome PID {managed.ProcessId} did not exit during cold start cleanup.");
            }
        }
        catch (ArgumentException)
        {
            // The process exited between inventory and cleanup.
        }
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

    private sealed record ManagedChromeProcess(int ProcessId, string UserDataDirectory);
}
