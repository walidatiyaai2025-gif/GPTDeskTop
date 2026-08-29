using GPTDeskTop.Models;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services;

/// <summary>
/// Minimal runtime surface required by crash recovery. The production adapter
/// delegates to Chrome/monitor services; tests can provide a deterministic
/// runtime while still exercising the real SQLite recovery orchestration.
/// </summary>
public interface ICrashRecoveryRuntime
{
    Task StopAllMonitorsAsync();
    Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken);
    void LaunchMonitorChrome(string? startUrl);
    Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken);
    Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken);
    Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken);
    Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class CrashRecoveryRuntimeAdapter : ICrashRecoveryRuntime
{
    private readonly ChromeDevToolsService _chrome;
    private readonly ChatGptMonitorService _monitorService;
    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();

    public CrashRecoveryRuntimeAdapter(ChromeDevToolsService chrome, ChatGptMonitorService monitorService)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
    }

    public Task StopAllMonitorsAsync() => _monitorService.StopAllAsync();
    public Task CloseAllMonitorTabsAsync(CancellationToken cancellationToken) => _chrome.CloseAllMonitorTabsAsync(cancellationToken);
    public void LaunchMonitorChrome(string? startUrl) => _chrome.LaunchMonitorChrome(startUrl);

    public async Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)
        => await _chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);

    public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)
        => _chrome.CreateTabAsync(url, cancellationToken);

    public Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken)
        => _outboundDelivery.SendOnceAsync(
            0,
            string.IsNullOrWhiteSpace(tab.Url) ? tab.Id : tab.Url,
            message,
            () => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken),
            null,
            cancellationToken);

    public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)
        => _monitorService.StartMonitorAsync(monitor, tab);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
