from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one match, found {count}')
    return text.replace(old, new, 1)

session_path = Path('src/GPTDeskTop/Services/SimpleMonitorProfileSession.cs')
safety_path = Path('src/GPTDeskTop/Services/SimpleMonitorSafetyGate.cs')
helper_path = Path('src/GPTDeskTop/Services/SimpleMonitorChromeOwnershipGate.cs')
test_path = Path('tests/GPTDeskTop.RuntimeTests/SimpleMonitorSingleChromeOwnershipRegressionTests.cs')

session = session_path.read_text(encoding='utf-8')
session = replace_once(
    session,
    '''        }, allowBrowserMutationRecovery: false);\n    }''',
    '''        }, allowBrowserMutationRecovery: false);\n        SimpleMonitorChromeOwnershipGate.Register(Chrome, Profile, DebuggingPort);\n    }''',
    'register selected managed Chrome')
session = replace_once(
    session,
    '''    public ValueTask DisposeAsync()\n    {\n        try { _launchedProcess?.Dispose(); } catch { }''',
    '''    public ValueTask DisposeAsync()\n    {\n        SimpleMonitorChromeOwnershipGate.Unregister(Chrome);\n        try { _launchedProcess?.Dispose(); } catch { }''',
    'unregister selected managed Chrome')
session_path.write_text(session, encoding='utf-8')

safety = safety_path.read_text(encoding='utf-8')
safety = replace_once(
    safety,
    '''        await PhysicalSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);\n        var release = true;\n        try\n        {\n            while (true)\n            {\n                await WaitForRateLimitClearAsync(chrome, tabResolver, status, cancellationToken).ConfigureAwait(false);\n                await WaitForQuietWindowAsync(status, cancellationToken).ConfigureAwait(false);\n\n                var tab = await tabResolver(cancellationToken).ConfigureAwait(false);''',
    '''        await PhysicalSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);\n        FileStream? crossProcessLease = null;\n        var release = true;\n        try\n        {\n            crossProcessLease = await SimpleMonitorChromeOwnershipGate.AcquireGlobalSendLeaseAsync(status, cancellationToken).ConfigureAwait(false);\n            while (true)\n            {\n                var ownershipTab = await tabResolver(cancellationToken).ConfigureAwait(false);\n                _ = await SimpleMonitorChromeOwnershipGate.EnsureExclusiveBeforeSendAsync(\n                    chrome, ownershipTab, status, cancellationToken).ConfigureAwait(false);\n\n                await WaitForRateLimitClearAsync(chrome, tabResolver, status, cancellationToken).ConfigureAwait(false);\n                await WaitForQuietWindowAsync(status, cancellationToken).ConfigureAwait(false);\n\n                var tab = await tabResolver(cancellationToken).ConfigureAwait(false);\n                tab = await SimpleMonitorChromeOwnershipGate.EnsureExclusiveBeforeSendAsync(\n                    chrome, tab, status, cancellationToken).ConfigureAwait(false);''',
    'pre-send ownership enforcement')
safety = replace_once(
    safety,
    '''                release = false;\n                return new SendPermit(this, tab, state);''',
    '''                release = false;\n                var transferredLease = crossProcessLease;\n                crossProcessLease = null;\n                return new SendPermit(this, tab, state, transferredLease);''',
    'transfer cross-process send lease')
safety = replace_once(
    safety,
    '''            if (release)\n                PhysicalSendGate.Release();''',
    '''            if (release)\n            {\n                crossProcessLease?.Dispose();\n                PhysicalSendGate.Release();\n            }''',
    'release failed acquisition lease')
safety = replace_once(
    safety,
    '''    internal sealed class SendPermit : IAsyncDisposable\n    {\n        private SimpleMonitorSafetyGate? _owner;\n\n        internal SendPermit(SimpleMonitorSafetyGate owner, ChromeTab tab, ChatPageState state)\n        {\n            _owner = owner;\n            Tab = tab;\n            State = state;\n        }''',
    '''    internal sealed class SendPermit : IAsyncDisposable\n    {\n        private SimpleMonitorSafetyGate? _owner;\n        private IDisposable? _crossProcessLease;\n\n        internal SendPermit(SimpleMonitorSafetyGate owner, ChromeTab tab, ChatPageState state, IDisposable? crossProcessLease)\n        {\n            _owner = owner;\n            _crossProcessLease = crossProcessLease;\n            Tab = tab;\n            State = state;\n        }''',
    'send permit lease state')
safety = replace_once(
    safety,
    '''        public ValueTask DisposeAsync()\n        {\n            var owner = Interlocked.Exchange(ref _owner, null);\n            owner?.ReleasePhysicalSendGate();\n            return ValueTask.CompletedTask;\n        }''',
    '''        public ValueTask DisposeAsync()\n        {\n            var lease = Interlocked.Exchange(ref _crossProcessLease, null);\n            try { lease?.Dispose(); } finally\n            {\n                var owner = Interlocked.Exchange(ref _owner, null);\n                owner?.ReleasePhysicalSendGate();\n            }\n            return ValueTask.CompletedTask;\n        }''',
    'send permit lease disposal')
safety_path.write_text(safety, encoding='utf-8')

helper_path.write_text(r'''using System.Runtime.CompilerServices;
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
                using (var writer = new StreamWriter(stream, leaveOpen: true))
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

    internal static async Task<ChromeTab> EnsureExclusiveBeforeSendAsync(ChromeDevToolsService selectedChrome, ChromeTab requestedTab, Action<string>? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedChrome);
        ArgumentNullException.ThrowIfNull(requestedTab);
        Registration registration;
        lock (RegistrationSync)
        {
            if (!Registrations.TryGetValue(selectedChrome, out registration!))
                throw new InvalidOperationException("Monitor Only Chrome ownership is not registered. Physical send is blocked.");
        }

        status?.Invoke("SINGLE CHROME GATE — verifying one GPTDeskTop Chrome before physical send...");
        foreach (var port in DiscoverManagedPorts(registration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port == registration.DebuggingPort) continue;
            if (!await IsEndpointAliveAsync(port, cancellationToken).ConfigureAwait(false)) continue;
            status?.Invoke($"SINGLE CHROME GATE — closing extra GPTDeskTop Chrome on CDP port {port} before send.");
            await CloseBrowserAtPortAsync(port, cancellationToken).ConfigureAwait(false);
            if (!await WaitForEndpointClosedAsync(port, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"Extra GPTDeskTop Chrome on CDP port {port} is still alive. Physical send is blocked.");
        }

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
''', encoding='utf-8')

test_path.write_text(r'''using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorSingleChromeOwnershipRegressionTests
{
    [Fact]
    public async Task Stable_port_contract_matches_profile_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "gptdesktop-single-chrome", Guid.NewGuid().ToString("N"));
        var profile = new ChromeProfileInfo("Profile 17", "Test", string.Empty, root, Path.Combine(root, "managed"));
        await using var session = new SimpleMonitorProfileSession(profile);
        Assert.Equal(SimpleMonitorChromeOwnershipGate.ResolveStablePort(profile.Key), session.DebuggingPort);
    }

    [Fact]
    public void Physical_send_gate_is_cross_process_and_single_chrome_fail_closed()
    {
        var root = FindRepositoryRoot();
        var safety = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs"));
        var ownership = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorChromeOwnershipGate.cs"));
        var session = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs"));
        Assert.Contains("AcquireGlobalSendLeaseAsync", safety, StringComparison.Ordinal);
        Assert.Contains("EnsureExclusiveBeforeSendAsync", safety, StringComparison.Ordinal);
        Assert.True(safety.IndexOf("EnsureExclusiveBeforeSendAsync", StringComparison.Ordinal) < safety.IndexOf("return new SendPermit", StringComparison.Ordinal));
        Assert.Contains("FileShare.None", ownership, StringComparison.Ordinal);
        Assert.Contains("ChromeProfileCatalog.Discover()", ownership, StringComparison.Ordinal);
        Assert.Contains("gptdesktop-profile-source.txt", ownership, StringComparison.Ordinal);
        Assert.Contains("CloseAllMonitorTabsAsync", ownership, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync", ownership, StringComparison.Ordinal);
        Assert.Contains("tabs.Count == 1", ownership, StringComparison.Ordinal);
        Assert.Contains("Physical send is blocked", ownership, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorChromeOwnershipGate.Register", session, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorChromeOwnershipGate.Unregister", session, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
''', encoding='utf-8')

print('v2.0.35 single-Chrome ownership patch applied')
