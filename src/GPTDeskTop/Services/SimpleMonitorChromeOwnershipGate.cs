using System.Diagnostics;
using System.Management;
using System.Runtime.CompilerServices;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

internal static class SimpleMonitorChromeOwnershipGate
{
    private const int LegacyDebuggingPort = 9222;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly DateTime CurrentAppStartUtc = SafeCurrentProcessStartUtc();
    private static readonly ConditionalWeakTable<ChromeDevToolsService, Registration> Registrations = new();
    private static readonly object RegistrationSync = new();

    internal static void Register(ChromeDevToolsService chrome, ChromeProfileInfo profile, int debuggingPort)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(profile);
        lock (RegistrationSync)
        {
            Registrations.Remove(chrome);
            Registrations.Add(chrome, new Registration(profile, debuggingPort));
        }
    }

    internal static void Unregister(ChromeDevToolsService chrome)
    {
        if (chrome is null) return;
        lock (RegistrationSync) Registrations.Remove(chrome);
    }

    internal static async Task<FileStream> AcquireGlobalSendLeaseAsync(Action<string>? status, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "SimpleMonitor");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "physical-send.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128, FileOptions.WriteThrough);
                stream.SetLength(0);
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 128, leaveOpen: true))
                {
                    writer.Write($"pid={Environment.ProcessId};utc={DateTimeOffset.UtcNow:O}");
                    writer.Flush();
                }
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                return stream;
            }
            catch (IOException)
            {
                status?.Invoke("SEND GATE — another GPTDeskTop process owns the physical sender. Waiting; no message will be sent.");
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static async Task CloseOtherManagedSessionsAsync(
        ChromeDevToolsService selectedChrome,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        var registration = GetRegistration(selectedChrome);

        // v2.0.41 closes the gap left by endpoint-only discovery. A Chrome left behind by an older
        // GPTDeskTop process can have a dead CDP endpoint while its visible browser process remains.
        // If Start Monitor then only checked ports it could legitimately create another managed
        // browser. Inspect Windows process command lines first and act only on user-data directories
        // under GPTDeskTop's own managed roots. Ordinary user Chrome is never a candidate.
        await ReconcileManagedChromeProcessesAsync(registration, status, cancellationToken).ConfigureAwait(false);

        if (await IsEndpointAliveAsync(registration.DebuggingPort, cancellationToken).ConfigureAwait(false))
            registration.EndpointEverSeen = true;

        foreach (var port in DiscoverManagedPorts(registration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port == registration.DebuggingPort) continue;
            if (!await IsEndpointAliveAsync(port, cancellationToken).ConfigureAwait(false)) continue;

            status?.Invoke($"SINGLE PROFILE — closing stale GPTDeskTop Chrome on CDP port {port}; selected profile remains on {registration.DebuggingPort}.");
            await CloseBrowserAtPortAsync(port, cancellationToken).ConfigureAwait(false);
            if (!await WaitForEndpointClosedAsync(port, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"A stale GPTDeskTop Chrome on CDP port {port} is still alive. The selected profile session cannot be made exclusive safely.");
        }
    }

    internal static async Task<ChromeTab> EnsureExclusiveBeforeSendAsync(ChromeDevToolsService selectedChrome, ChromeTab requestedTab, Action<string>? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedChrome);
        ArgumentNullException.ThrowIfNull(requestedTab);
        _ = GetRegistration(selectedChrome);

        status?.Invoke("SINGLE CHROME GATE — verifying one GPTDeskTop Chrome before physical send...");
        await CloseOtherManagedSessionsAsync(selectedChrome, status, cancellationToken).ConfigureAwait(false);

        var tabs = await selectedChrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var keeper = ResolveKeeper(tabs, requestedTab)
            ?? throw new InvalidOperationException("The selected Monitor Only Chrome target disappeared before send. Physical send is blocked.");
        var extras = tabs.Where(tab => !string.Equals(tab.Id, keeper.Id, StringComparison.Ordinal)).ToArray();
        foreach (var extra in extras)
        {
            status?.Invoke($"SINGLE CHROME GATE — closing extra app Chrome tab/window '{Compact(extra.Title)}' before send.");
            _ = await selectedChrome.CloseTabAsync(extra, cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tabs = await selectedChrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            keeper = ResolveKeeper(tabs, keeper);
            if (keeper is not null && tabs.Count == 1)
            {
                status?.Invoke("SINGLE CHROME GATE — verified: exactly one GPTDeskTop Chrome target is active. Send may proceed.");
                return keeper;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Single-Chrome invariant failed before send: selected endpoint has {tabs.Count} controllable page targets. Physical send is blocked.");
    }

    internal static int ResolveStablePort(string key)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return 12000 + (int)(hash % 10000);
        }
    }

    private static Registration GetRegistration(ChromeDevToolsService chrome)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        lock (RegistrationSync)
        {
            if (Registrations.TryGetValue(chrome, out var registration)) return registration;
        }
        throw new InvalidOperationException("Monitor Only Chrome ownership is not registered. Browser mutation is blocked.");
    }

    private static async Task ReconcileManagedChromeProcessesAsync(
        Registration registration,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedChromeProcess> processes;
        try
        {
            processes = DiscoverManagedChromeProcesses();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "GPTDeskTop could not verify the managed Chrome process inventory. Start/recovery is blocked rather than risk opening a second Chrome.",
                ex);
        }

        if (processes.Count == 0) return;

        var selectedDirectory = NormalizeDirectory(registration.Profile.ManagedUserDataDirectory);
        var selectedEndpointAlive = await IsEndpointAliveAsync(registration.DebuggingPort, cancellationToken).ConfigureAwait(false);
        if (selectedEndpointAlive) registration.EndpointEverSeen = true;

        foreach (var managed in processes.OrderBy(process => process.ProcessId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedProcess = PathsEqual(managed.UserDataDirectory, selectedDirectory);
            if (selectedProcess)
            {
                if (selectedEndpointAlive || registration.EndpointEverSeen)
                {
                    // This is the selected browser identity for the current monitor run. Even if CDP
                    // disappears later, runtime recovery is passive and this process is never killed
                    // merely to obtain a new browser.
                    continue;
                }

                if (managed.StartUtc >= CurrentAppStartUtc - TimeSpan.FromSeconds(2))
                {
                    throw new InvalidOperationException(
                        $"The selected GPTDeskTop Monitor Chrome process PID {managed.ProcessId} is already running but CDP {registration.DebuggingPort} is unavailable. A second Chrome will not be opened. Close/restore that managed browser or Stop/Start after it exits.");
                }

                status?.Invoke($"SINGLE CHROME — removing stale selected GPTDeskTop Chrome PID {managed.ProcessId} left by an earlier app session before the one allowed Start Monitor launch.");
                await KillManagedProcessTreeAsync(managed, cancellationToken).ConfigureAwait(false);
                continue;
            }

            status?.Invoke($"SINGLE CHROME — closing stale GPTDeskTop-managed Chrome PID {managed.ProcessId} for another managed profile. Ordinary Chrome is untouched.");
            await KillManagedProcessTreeAsync(managed, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ManagedChromeProcess> DiscoverManagedChromeProcesses()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<ManagedChromeProcess>();

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
            results.Add(new ManagedChromeProcess(processId, directory, SafeProcessStartUtc(processId)));
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

    private static async Task KillManagedProcessTreeAsync(ManagedChromeProcess managed, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(managed.ProcessId);
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(CloseTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException(
                    $"GPTDeskTop-managed Chrome PID {managed.ProcessId} did not exit. A new Monitor Chrome is blocked to preserve the single-browser invariant.");
            }
        }
        catch (ArgumentException)
        {
            // Process already exited between inventory and cleanup.
        }
    }

    private static IReadOnlyList<int> DiscoverManagedPorts(Registration selected)
    {
        var ports = new HashSet<int> { selected.DebuggingPort };
        foreach (var profile in ChromeProfileCatalog.Discover())
        {
            var marker = Path.Combine(profile.ManagedUserDataDirectory, "gptdesktop-profile-source.txt");
            if (File.Exists(marker)) ports.Add(ResolveStablePort(profile.Key));
        }
        var legacyProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTDeskTop", "ChromeProfile");
        if (Directory.Exists(legacyProfile)) ports.Add(LegacyDebuggingPort);
        return ports.OrderBy(port => port).ToArray();
    }

    private static ChromeTab? ResolveKeeper(IReadOnlyList<ChromeTab> tabs, ChromeTab requested)
    {
        var exact = tabs.FirstOrDefault(tab => string.Equals(tab.Id, requested.Id, StringComparison.Ordinal));
        if (exact is not null) return exact;
        if (SimpleMonitorProfileSession.TryGetConversationId(requested.Url, out _))
            return tabs.FirstOrDefault(tab => SimpleMonitorProfileSession.SameConversation(tab.Url, requested.Url));
        return null;
    }

    private static async Task<bool> IsEndpointAliveAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = ProbeTimeout };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/json/version", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return false; }
    }

    private static async Task CloseBrowserAtPortAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var chrome = new ChromeDevToolsService(client, new ChromeConfig
        {
            DebuggingPort = port,
            DebuggingBaseUrl = $"http://127.0.0.1:{port}",
            StartUrl = "https://chatgpt.com/"
        }, allowBrowserMutationRecovery: false);
        await chrome.CloseAllMonitorTabsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForEndpointClosedAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + CloseTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsEndpointAliveAsync(port, cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
        return !await IsEndpointAliveAsync(port, cancellationToken).ConfigureAwait(false);
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

    private static string Compact(string? value)
    {
        var text = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 80 ? text : text[..80];
    }

    private sealed class Registration
    {
        internal Registration(ChromeProfileInfo profile, int debuggingPort)
        {
            Profile = profile;
            DebuggingPort = debuggingPort;
        }

        internal ChromeProfileInfo Profile { get; }
        internal int DebuggingPort { get; }
        internal bool EndpointEverSeen { get; set; }
    }

    private sealed record ManagedChromeProcess(int ProcessId, string UserDataDirectory, DateTime StartUtc);
}
