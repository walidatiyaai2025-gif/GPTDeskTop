using System.Runtime.CompilerServices;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

internal static class SimpleMonitorChromeOwnershipGate
{
    private const int LegacyDebuggingPort = 9222;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(4);
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

    private static string Compact(string? value)
    {
        var text = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 80 ? text : text[..80];
    }

    private sealed record Registration(ChromeProfileInfo Profile, int DebuggingPort);
}
