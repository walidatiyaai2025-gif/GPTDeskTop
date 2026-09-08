using System.Diagnostics;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class SimpleMonitorProfileSession : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly object _freshTargetSync = new();
    private readonly Dictionary<string, HashSet<string>> _freshTargetBaselines = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private Process? _launchedProcess;
    private bool _startLaunchAuthorized;
    private DateTimeOffset? _lastEndpointSeenUtc;

    public ChromeProfileInfo Profile { get; }
    public ChromeDevToolsService Chrome { get; }
    public int DebuggingPort { get; }

    public SimpleMonitorProfileSession(ChromeProfileInfo profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        DebuggingPort = ResolveStablePort(profile.Key);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Chrome = new ChromeDevToolsService(_httpClient, new ChromeConfig
        {
            DebuggingPort = DebuggingPort,
            DebuggingBaseUrl = $"http://127.0.0.1:{DebuggingPort}",
            StartUrl = "https://chatgpt.com/",
            SmartAutoFollowEnabled = true,
            SmartAutoFollowThrottleMilliseconds = 400,
            SmartAutoFollowNearBottomPixels = 180
        }, allowBrowserMutationRecovery: false);
        SimpleMonitorChromeOwnershipGate.Register(Chrome, Profile, DebuggingPort);
    }

    /// <summary>
    /// Passive pre-start attachment probe. This method deliberately NEVER starts Chrome.
    /// The Monitor Only UI may call it while loading, selecting a profile, connecting, refreshing,
    /// or inspecting state without creating any browser process.
    /// </summary>
    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        _ = await TryEnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Passive attachment probe that reports whether the selected managed CDP browser is really
    /// reachable. It never starts Chrome. UI code can use this to avoid showing a false Connected
    /// state when only a source profile has been selected.
    /// </summary>
    public Task<bool> TryEnsureConnectedAsync(CancellationToken cancellationToken = default)
        => CanReadEndpointAsync(cancellationToken);

    public Task<bool> IsAutomationSessionAvailableAsync(CancellationToken cancellationToken = default)
        => CanReadEndpointAsync(cancellationToken);

    public async Task<IReadOnlyList<ChromeTab>> GetConversationTabsAsync(CancellationToken cancellationToken = default)
    {
        if (!await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false))
            return Array.Empty<ChromeTab>();

        var tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        return tabs
            .Where(tab => TryGetConversationId(tab.Url, out _))
            .OrderBy(tab => tab.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<ChromeTab?> ResolveConversationAsync(
        string conversationUrl,
        bool openIfMissing,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetConversationId(conversationUrl, out var expectedId)) return null;

        // openIfMissing:true is the explicit Start Monitor authorization boundary. It latches
        // launch permission onto this exact selected profile/session for the lifetime of this
        // running Monitor Only session. Passive/read-only calls never set that authorization.
        if (openIfMissing)
        {
            _startLaunchAuthorized = true;
            await EnsureStartAuthorizedBrowserAvailableAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var existing = tabs.FirstOrDefault(tab =>
            TryGetConversationId(tab.Url, out var actualId)
            && string.Equals(expectedId, actualId, StringComparison.Ordinal));
        if (existing is not null) return existing;
        if (!openIfMissing) return null;

        var created = await Chrome.CreateTabAsync(conversationUrl, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentTabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            var resolved = currentTabs.FirstOrDefault(tab =>
                TryGetConversationId(tab.Url, out var actualId)
                && string.Equals(expectedId, actualId, StringComparison.Ordinal));
            if (resolved is not null) return resolved;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return TryGetConversationId(created.Url, out var createdId)
               && string.Equals(expectedId, createdId, StringComparison.Ordinal)
            ? created
            : null;
    }

    /// <summary>
    /// Creates a brand-new ChatGPT target in an already-connected selected profile. This method
    /// never launches or relaunches Chrome. Runtime recovery also remains passive once this selected
    /// CDP endpoint has ever been observed.
    /// </summary>
    public async Task<ChromeTab> CreateFreshConversationTabAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAttachedOrThrowAsync(cancellationToken).ConfigureAwait(false);
        var existingTabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var baseline = existingTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var created = await Chrome.CreateNewChatTabAsync(cancellationToken).ConfigureAwait(false);
        lock (_freshTargetSync)
            _freshTargetBaselines[created.Id] = baseline;
        return created;
    }

    /// <summary>
    /// Clean recovery for a monitor worker that was already started explicitly. It never touches
    /// normal Chrome and it never starts/restarts a Chrome process. Only other GPTDeskTop-managed
    /// automation sessions are closed, then ChatGPT tabs on this exact selected managed endpoint
    /// are cleaned if and only if the same endpoint is reachable again.
    /// </summary>
    public async Task RecoverAfterAuthorizedStartAsync(
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!_startLaunchAuthorized)
            throw new InvalidOperationException("Chrome recovery is not authorized until Start Monitor is explicitly pressed for this selected profile.");

        status?.Invoke($"CLEAN RECOVERY — keeping selected profile '{Profile.DisplayLabel}' on CDP {DebuggingPort} and closing stale GPTDeskTop profile sessions.");
        await EnsureStartAuthorizedBrowserAvailableAsync(cancellationToken, status).ConfigureAwait(false);

        status?.Invoke("CLEAN RECOVERY — closing all ChatGPT tabs owned by the selected GPTDeskTop automation session only.");
        await CloseAutomationOwnedChatTabsAsync(cancellationToken).ConfigureAwait(false);

        if (!await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false))
        {
            status?.Invoke("CLEAN RECOVERY — selected automation endpoint is temporarily unavailable; waiting for the same session. Runtime Chrome auto-launch is disabled.");
            await EnsureStartAuthorizedBrowserAvailableAsync(cancellationToken, status).ConfigureAwait(false);
        }
    }

    public async Task CloseAutomationOwnedChatTabsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAttachedOrThrowAsync(cancellationToken).ConfigureAwait(false);
        var tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        if (tabs.Count == 0) return;

        // Keep the selected managed browser alive while removing ChatGPT state. Never mutate tabs
        // on another endpoint and never kill the user's ordinary Chrome process.
        if (!tabs.Any(tab => !IsChatGptPage(tab.Url)))
        {
            _ = await Chrome.CreateTabAsync("about:blank", cancellationToken).ConfigureAwait(false);
            tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tab in tabs.Where(tab => IsChatGptPage(tab.Url)).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = await Chrome.CloseTabAsync(tab, cancellationToken).ConfigureAwait(false);
        }

        lock (_freshTargetSync) _freshTargetBaselines.Clear();
    }

    /// <summary>
    /// Refreshes the mutable tab snapshot from Chrome without navigating, reloading, launching,
    /// or relaunching a browser process.
    /// </summary>
    public async Task<ChromeTab?> RefreshLiveTabAsync(
        ChromeTab tab,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);
        await EnsureAttachedOrThrowAsync(cancellationToken).ConfigureAwait(false);
        var tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
        var live = tabs.FirstOrDefault(candidate => string.Equals(candidate.Id, tab.Id, StringComparison.Ordinal));
        if (live is null && TryGetConversationId(tab.Url, out var expectedId))
        {
            live = tabs.FirstOrDefault(candidate =>
                TryGetConversationId(candidate.Url, out var actualId)
                && string.Equals(expectedId, actualId, StringComparison.Ordinal));
        }
        if (live is null) return null;
        CopyTab(tab, live);
        return tab;
    }

    /// <summary>
    /// Waits for a newly-created root ChatGPT target to acquire its stable conversation URL after
    /// the first confirmed send. Uses the repository's existing new-chat stable-target selector so
    /// CDP target replacement during navigation is handled without binding to an older chat.
    /// </summary>
    public async Task<ChromeTab?> WaitForStableConversationAsync(
        ChromeTab tab,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);
        HashSet<string> baseline;
        lock (_freshTargetSync)
            baseline = _freshTargetBaselines.TryGetValue(tab.Id, out var stored)
                ? new HashSet<string>(stored, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                var stable = NewChatStableTargetSelector.Select(tab, baseline, tabs);
                if (stable is not null && TryGetConversationId(stable.Url, out _))
                {
                    var originalId = tab.Id;
                    CopyTab(tab, stable);
                    lock (_freshTargetSync)
                    {
                        _freshTargetBaselines.Remove(originalId);
                        _freshTargetBaselines.Remove(tab.Id);
                    }
                    return tab;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ChromeTransportFailureClassifier.IsTransient(ex))
            {
                // The first-send navigation may briefly replace/rebind the CDP target.
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        lock (_freshTargetSync) _freshTargetBaselines.Remove(tab.Id);
        return null;
    }

    public static bool SameConversation(string left, string right)
        => TryGetConversationId(left, out var leftId)
           && TryGetConversationId(right, out var rightId)
           && string.Equals(leftId, rightId, StringComparison.Ordinal);

    public static bool TryGetConversationId(string? url, out string conversationId)
    {
        conversationId = string.Empty;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "c", StringComparison.OrdinalIgnoreCase))
            return false;

        conversationId = segments[1];
        return conversationId.Length > 0;
    }

    private async Task EnsureAttachedOrThrowAsync(CancellationToken cancellationToken)
    {
        if (await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false)) return;

        throw new InvalidOperationException(
            "The Monitor Only Chrome automation session is no longer available. The running monitor will preserve its pending message and use passive same-session recovery instead of opening another Chrome.");
    }

    private async Task EnsureStartAuthorizedBrowserAvailableAsync(
        CancellationToken cancellationToken,
        Action<string>? status = null)
    {
        if (!_startLaunchAuthorized)
            throw new InvalidOperationException("Chrome launch is not authorized until Start Monitor is explicitly pressed for this selected profile.");

        await SimpleMonitorChromeOwnershipGate.CloseOtherManagedSessionsAsync(Chrome, status, cancellationToken).ConfigureAwait(false);
        if (await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false)) return;

        // Once this exact selected CDP endpoint has ever been observed, Start Monitor is considered
        // bound to that browser identity. A later transient or permanent endpoint loss is NEVER
        // authority to Process.Start another Chrome. The runner stays alive with the message pending
        // and retries this same endpoint in its bounded/15-minute recovery loop.
        if (_lastEndpointSeenUtc is not null)
        {
            status?.Invoke($"Selected Chrome session on CDP {DebuggingPort} is unavailable. Waiting for the same endpoint; runtime Chrome auto-launch is disabled.");
            if (await WaitForExistingEndpointAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false)) return;

            throw new TimeoutException(
                $"The selected GPTDeskTop Chrome session on CDP {DebuggingPort} is still unavailable. No new Chrome was opened. Start Monitor remains responsible for preserving the pending message and retrying the same session.");
        }

        // The only legal Process.Start opportunity: the operator explicitly pressed Start Monitor
        // and this session has never observed a compatible managed CDP endpoint at all.
        await LaunchChromeForMonitorStartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The single Chrome process-launch boundary for Monitor Only. It is reachable only during the
    /// initial explicit Start Monitor transition before this session has ever observed its selected
    /// CDP endpoint. Runtime recovery can never reach Process.Start after a successful attachment.
    /// </summary>
    private async Task LaunchChromeForMonitorStartAsync(CancellationToken cancellationToken)
    {
        if (!_startLaunchAuthorized)
            throw new InvalidOperationException("Start Monitor authorization is required before Chrome can be launched.");

        await _launchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false)) return;

            // Once this Monitor Only session has launched a managed Chrome process, an endpoint
            // hiccup must never kill/relaunch that live process. Keep the pending message and let
            // recovery retry the same process identity instead.
            if (IsLaunchedProcessAlive())
            {
                if (await WaitForExistingEndpointAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false)) return;
                throw new TimeoutException(
                    $"The selected GPTDeskTop Chrome process is still running, but CDP {DebuggingPort} is temporarily unavailable. No second Chrome was opened; the monitor will preserve its pending message and retry the same session.");
            }

            DisposeExitedLaunchedProcess();

            Directory.CreateDirectory(Profile.ManagedUserDataDirectory);
            File.WriteAllText(
                Path.Combine(Profile.ManagedUserDataDirectory, "gptdesktop-profile-source.txt"),
                $"ChromeProfile={Profile.Key}{Environment.NewLine}DisplayName={Profile.DisplayName}{Environment.NewLine}SourceDirectory={Profile.SourceDirectory}{Environment.NewLine}");

            var chromePath = FindChromePath();
            var arguments = string.Join(' ', new[]
            {
                $"--remote-debugging-port={DebuggingPort}",
                $"--user-data-dir=\"{Profile.ManagedUserDataDirectory}\"",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                "--disable-features=CalculateNativeWinOcclusion",
                "\"https://chatgpt.com/\""
            });

            // Deliberately omit --new-window. If Chrome already owns this managed user-data
            // directory, the command is handed to that existing instance rather than forcing a
            // second top-level window. A cold start still opens the one authorized monitor window.
            _launchedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = arguments,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Chrome could not be started for the selected profile.");

            if (await WaitForExistingEndpointAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false)) return;

            throw new TimeoutException(
                $"Chrome profile '{Profile.DisplayLabel}' opened after Start Monitor, but its automation endpoint did not become ready. No duplicate Chrome will be launched while this process remains alive.");
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private async Task<bool> WaitForExistingEndpointAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CanReadEndpointAsync(cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private bool IsLaunchedProcessAlive()
    {
        if (_launchedProcess is null) return false;
        try { return !_launchedProcess.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private void DisposeExitedLaunchedProcess()
    {
        if (_launchedProcess is null) return;
        try
        {
            if (!_launchedProcess.HasExited) return;
        }
        catch (InvalidOperationException) { }

        try { _launchedProcess.Dispose(); } catch { }
        _launchedProcess = null;
    }

    private static bool IsChatGptPage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "chat.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyTab(ChromeTab target, ChromeTab source)
    {
        target.Id = source.Id;
        target.Title = source.Title;
        target.Url = source.Url;
        target.Type = source.Type;
        target.WebSocketDebuggerUrl = source.WebSocketDebuggerUrl;
    }

    private async Task<bool> CanReadEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await Chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
            _lastEndpointSeenUtc = DateTimeOffset.UtcNow;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return false;
        }
    }

    private static int ResolveStablePort(string key)
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

    public ValueTask DisposeAsync()
    {
        _startLaunchAuthorized = false;
        SimpleMonitorChromeOwnershipGate.Unregister(Chrome);
        try { _launchedProcess?.Dispose(); } catch { }
        _launchedProcess = null;
        lock (_freshTargetSync) _freshTargetBaselines.Clear();
        _launchGate.Dispose();
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}