using GPTDeskTop.Models;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Connects the development-plan engine to a specific saved Monitor/Chrome tab.
/// The engine can advance only after the monitor's verified sender confirms receipt.
/// </summary>
public sealed class MonitorDevelopmentTaskBridge : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly ChatGptMonitorService _monitorService;
    private readonly SavedMonitor _monitor;
    private readonly ChromeTab _tab;
    private DevelopmentTaskDeliveryCoordinator? _coordinator;
    private bool _disposed;

    public MonitorDevelopmentTaskBridge(
        DevelopmentTaskEngine engine,
        ChatGptMonitorService monitorService,
        SavedMonitor monitor,
        ChromeTab tab)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
    }

    public string MonitorId => _monitor.Id.ToString();
    public int TabId => _tab.Id;

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _coordinator ??= new DevelopmentTaskDeliveryCoordinator(
            _engine,
            (message, cancellationToken) => SendVerifiedAsync(message, cancellationToken));
    }

    private async Task<bool> SendVerifiedAsync(string message, CancellationToken cancellationToken)
    {
        if (_disposed) return false;
        if (!_monitorService.IsMonitorRunning(_monitor.Id)) return false;
        return await _monitorService.SendDevelopmentTaskMessageVerifiedAsync(
            _monitor.Id,
            _tab,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_coordinator is not null) await _coordinator.DisposeAsync().ConfigureAwait(false);
    }
}
