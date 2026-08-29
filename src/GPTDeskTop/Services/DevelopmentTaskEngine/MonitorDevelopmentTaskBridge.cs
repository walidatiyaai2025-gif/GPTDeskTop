using GPTDeskTop.Models;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Connects the development-plan engine to a specific saved Monitor/Chrome tab.
/// The engine can advance only after the monitor is running and CDP verifies receipt.
/// </summary>
public sealed class MonitorDevelopmentTaskBridge : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly ChatGptMonitorService _monitorService;
    private readonly ChromeDevToolsService _chrome;
    private readonly SavedMonitor _monitor;
    private readonly ChromeTab _tab;
    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();
    private DevelopmentTaskDeliveryCoordinator? _coordinator;
    private bool _disposed;

    public MonitorDevelopmentTaskBridge(
        DevelopmentTaskEngine engine,
        ChatGptMonitorService monitorService,
        ChromeDevToolsService chrome,
        SavedMonitor monitor,
        ChromeTab tab)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
    }

    public string MonitorId => _monitor.Id.ToString();
    public string TabId => _tab.Id;

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _coordinator ??= new DevelopmentTaskDeliveryCoordinator(
            _engine,
            SendVerifiedAsync,
            CheckpointDeliveredAsync);
    }

    private Task<bool> SendVerifiedAsync(string message, CancellationToken cancellationToken)
    {
        if (_disposed || !_monitorService.IsMonitorRunning(_monitor.Id))
            return Task.FromResult(false);

        return _outboundDelivery.SendOnceAsync(
            _monitor.Id,
            string.IsNullOrWhiteSpace(_tab.Url) ? _tab.Id : _tab.Url,
            message,
            () => _chrome.SendChatMessageVerifiedAsync(_tab, message, cancellationToken),
            null,
            cancellationToken);
    }

    private Task CheckpointDeliveredAsync(string message, CancellationToken cancellationToken)
    {
        return _engine.CheckpointDeliveredAsync(
            MonitorId,
            TabId,
            DevelopmentTaskDeliveryCoordinator.Fingerprint(message),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_coordinator is not null) await _coordinator.DisposeAsync().ConfigureAwait(false);
    }
}
