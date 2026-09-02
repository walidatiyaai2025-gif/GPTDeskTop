using System.Diagnostics;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed class SimpleMonitorProfileSession : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private Process? _launchedProcess;

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
        });
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (await CanReadEndpointAsync(cancellationToken)) return;

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
            "--new-window",
            "\"https://chatgpt.com/\""
        });

        _launchedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = arguments,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Chrome could not be started for the selected profile.");

        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CanReadEndpointAsync(cancellationToken)) return;
            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"Chrome profile '{Profile.DisplayLabel}' opened, but its automation endpoint did not become ready. Close any conflicting automation window for this profile and retry.");
    }

    public async Task<IReadOnlyList<ChromeTab>> GetConversationTabsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var tabs = await Chrome.GetTabsAsync(cancellationToken);
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
        await EnsureConnectedAsync(cancellationToken);

        var tabs = await Chrome.GetTabsAsync(cancellationToken);
        var existing = tabs.FirstOrDefault(tab =>
            TryGetConversationId(tab.Url, out var actualId)
            && string.Equals(expectedId, actualId, StringComparison.Ordinal));
        if (existing is not null) return existing;
        if (!openIfMissing) return null;

        var created = await Chrome.CreateTabAsync(conversationUrl, cancellationToken);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentTabs = await Chrome.GetTabsAsync(cancellationToken);
            var resolved = currentTabs.FirstOrDefault(tab =>
                TryGetConversationId(tab.Url, out var actualId)
                && string.Equals(expectedId, actualId, StringComparison.Ordinal));
            if (resolved is not null) return resolved;
            await Task.Delay(250, cancellationToken);
        }

        return TryGetConversationId(created.Url, out var createdId)
               && string.Equals(expectedId, createdId, StringComparison.Ordinal)
            ? created
            : null;
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

    private async Task<bool> CanReadEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await Chrome.GetTabsAsync(cancellationToken);
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
        try { _launchedProcess?.Dispose(); } catch { }
        _launchedProcess = null;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
