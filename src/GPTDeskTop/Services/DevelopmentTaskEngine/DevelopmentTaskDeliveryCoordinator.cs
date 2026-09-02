using System.Security.Cryptography;
using System.Text;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Bridges DevelopmentTaskEngine message emission to a verified chat sender.
/// Verified delivery is only the outbound half of a plan step; the plan advances
/// later when the canonical monitor reports a stable assistant response.
/// </summary>
public sealed class DevelopmentTaskDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly Func<string, CancellationToken, Task<bool>> _verifiedSender;
    private readonly Func<string, CancellationToken, Task>? _checkpointAfterDelivery;
    private readonly string? _responseMonitorId;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private bool _messageDeliveredThisWindow;
    private bool _disposed;

    public event Action<string>? DeliverySucceeded;
    public event Action<string>? DeliveryFailed;

    public DevelopmentTaskDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        Func<string, CancellationToken, Task<bool>> verifiedSender,
        Func<string, CancellationToken, Task>? checkpointAfterDelivery = null,
        string? responseMonitorId = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _verifiedSender = verifiedSender ?? throw new ArgumentNullException(nameof(verifiedSender));
        _checkpointAfterDelivery = checkpointAfterDelivery;
        _responseMonitorId = responseMonitorId;
        _engine.MessageReady += OnMessageReady;
        _engine.CoolingCompleted += OnCoolingCompleted;
    }

    private void OnMessageReady(string message)
    {
        if (_disposed || _messageDeliveredThisWindow) return;
        _ = DeliverAsync(message);
    }

    private void OnCoolingCompleted(object? sender, EventArgs e) => _messageDeliveredThisWindow = false;

    private async Task DeliverAsync(string message)
    {
        if (_disposed || _messageDeliveredThisWindow) return;
        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || _messageDeliveredThisWindow) return;
            var sent = await _verifiedSender(message, CancellationToken.None).ConfigureAwait(false);
            if (!sent)
            {
                await _engine.ReportDeliveryFailureAsync("Development message delivery could not be verified; the plan position was preserved for retry.").ConfigureAwait(false);
                DeliveryFailed?.Invoke(message);
                return;
            }

            _messageDeliveredThisWindow = true;

            if (_checkpointAfterDelivery is not null)
                await _checkpointAfterDelivery(message, CancellationToken.None).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_responseMonitorId))
                await _engine.MarkAwaitingAssistantResponseAsync([_responseMonitorId]).ConfigureAwait(false);

            DeliverySucceeded?.Invoke(message);
        }
        catch (Exception ex)
        {
            await _engine.ReportDeliveryFailureAsync(ex.Message).ConfigureAwait(false);
            DeliveryFailed?.Invoke(message);
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    public static string Fingerprint(string message) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(message)));

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _engine.MessageReady -= OnMessageReady;
        _engine.CoolingCompleted -= OnCoolingCompleted;
        _deliveryGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
