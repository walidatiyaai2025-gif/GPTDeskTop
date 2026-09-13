using System.Diagnostics;
using System.Management;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Narrow owner-authorized recovery for a persistent passive Runtime.evaluate timeout.
/// It is deliberately not a generic transport recovery: ordinary Chrome is never touched,
/// and callers must invoke it only before a physical send or after a durable send checkpoint.
/// </summary>
internal static class RuntimeEvaluateTimeoutRecoveryService
{
    internal static readonly TimeSpan RestartDelay = TimeSpan.FromMinutes(1);

    private const string ProfileSourceMarker = "gptdesktop-profile-source.txt";
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EndpointReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task RestartAfterDelayAsync(
        SimpleMonitorProfileSession session,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Managed Chrome restart is supported only on Windows.");

        var selectedDirectory = NormalizeDirectory(session.Profile.ManagedUserDataDirectory);
        if (!MonitorOnlyManagedChromeGuard.HasExplicitStartAuthorization(selectedDirectory))
        {
            throw new InvalidOperationException(
                "Runtime.evaluate recovery is blocked because the selected managed Chrome profile does not have explicit Start Monitor authorization.");
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            status?.Invoke("Runtime.evaluate timeout persisted. Waiting 60 seconds before managed Chrome recovery; Start Monitor remains running and no pending message will be resent during the wait.");
            await Task.Delay(RestartDelay, cancellationToken).ConfigureAwait(false);

            status?.Invoke("Runtime.evaluate recovery — closing GPTDeskTop-managed Chrome processes only. Ordinary Chrome is untouched.");
            await KillAllManagedChromeProcessesAsync(selectedDirectory, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(selectedDirectory);
            File.WriteAllText(
                Path.Combine(selectedDirectory, ProfileSourceMarker),
                $"ChromeProfile={session.Profile.Key}{Environment.NewLine}DisplayName={session.Profile.DisplayName}{Environment.NewLine}SourceDirectory={session.Profile.SourceDirectory}{Environment.NewLine}");

            var chromePath = FindChromePath();
            var arguments = string.Join(' ', new[]
            {
                $"--remote-debugging-port={session.DebuggingPort}",
                $"--user-data-dir=\"{selectedDirectory}\"",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-features=CalculateNativeWinOcclusion",
                "\"https://chatgpt.com/\""
            });

            status?.Invoke($"Runtime.evaluate recovery — starting one clean GPTDeskTop-managed Chrome for '{session.Profile.DisplayLabel}' on CDP {session.DebuggingPort}.");
            using var launched = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = arguments,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Chrome could not be restarted for the selected GPTDeskTop profile.");

            await WaitForEndpointReadyAsync(session.DebuggingPort, cancellationToken).ConfigureAwait(false);
            await session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            status?.Invoke("Runtime.evaluate recovery complete — one managed Chrome is ready. Start Monitor will resume automatically from the existing pending/checkpoint state.");
            RuntimeFlightRecorder.Record(
                "SimpleMonitor",
                "RuntimeEvaluateManagedChromeRestarted",
                $"cdp={session.DebuggingPort}; delaySeconds={(int)RestartDelay.TotalSeconds}",
                selectedDirectory);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task KillAllManagedChromeProcessesAsync(
        string selectedDirectory,
        CancellationToken cancellationToken)
    {
        var managedDirectories = ChromeProfileCatalog.Discover()
            .Select(profile => NormalizeDirectory(profile.ManagedUserDataDirectory))
            .Append(NormalizeDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GPTDeskTop",
                "ChromeProfile")))
            .Append(selectedDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<ManagedChromeProcess> managed;
        try
        {
            managed = DiscoverManagedChromeProcesses(managedDirectories);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "GPTDeskTop could not verify the managed Chrome process inventory, so Runtime.evaluate recovery was blocked before any browser restart.",
                ex);
        }

        foreach (var candidate in managed.OrderBy(item => item.ProcessId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await KillManagedProcessTreeAsync(candidate, cancellationToken).ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow + ProcessExitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DiscoverManagedChromeProcesses(managedDirectories).Count == 0) return;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        var survivors = DiscoverManagedChromeProcesses(managedDirectories);
        if (survivors.Count > 0)
        {
            throw new InvalidOperationException(
                $"{survivors.Count} GPTDeskTop-managed Chrome process(es) remained after the recovery kill boundary. A replacement Chrome was not started.");
        }
    }

    private static IReadOnlyList<ManagedChromeProcess> DiscoverManagedChromeProcesses(
        IReadOnlyList<string> managedDirectories)
    {
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

    private static async Task KillManagedProcessTreeAsync(
        ManagedChromeProcess managed,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(managed.ProcessId);
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(ProcessExitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException(
                    $"GPTDeskTop-managed Chrome PID {managed.ProcessId} did not exit. Replacement launch is blocked to avoid duplicate managed Chrome.");
            }
        }
        catch (ArgumentException)
        {
            // The process exited between inventory and cleanup.
        }
        catch (InvalidOperationException) when (!ProcessStillExists(managed.ProcessId))
        {
            // The process exited between inventory and cleanup.
        }
    }

    private static bool ProcessStillExists(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForEndpointReadyAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + EndpointReadyTimeout;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(
                    $"http://127.0.0.1:{port}/json/version",
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The clean GPTDeskTop-managed Chrome restarted, but CDP {port} did not become ready within {(int)EndpointReadyTimeout.TotalSeconds} seconds.");
    }

    private static string FindChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome was not found on this machine.");
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record ManagedChromeProcess(int ProcessId, string UserDataDirectory);
}
