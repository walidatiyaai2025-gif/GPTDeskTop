using System.Security.Cryptography;
using System.Text;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Bridges DevelopmentTaskEngine message emission to a verified chat sender.
/// A task advances only after the sender confirms that the message was accepted.
/// At most one verified message is delivered during each work window.
/// </summary>
public sealed class DevelopmentTaskDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly Func<string, CancellationToken, Task<bool>> _verifiedSender;
    private readonly Func<string, CancellationToken, Task>? _checkpointAfterDelivery;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private bool _messageDeliveredThisWindow;
    private bool _disposed;

    public event Action<string>? DeliverySucceeded;
    public event Action<string>? DeliveryFailed;

    public DevelopmentTaskDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        Func<string, CancellationToken, Task<bool>> verifiedSender,
        Func<string, CancellationToken, Task>? checkpointAfterDelivery = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _verifiedSender = verifiedSender ?? throw new ArgumentNullException(nameof(verifiedSender));
        _checkpointAfterDelivery = checkpointAfterDelivery;
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
                DeliveryFailed?.Invoke(message);
                return;
            }

            // Delivery is already externally observable at this point. Mark the
            // current work window before any asynchronous checkpoint/advance so
            // a fast engine loop cannot schedule the next message in this window.
            _messageDeliveredThisWindow = true;

            if (_checkpointAfterDelivery is not null)
                await _checkpointAfterDelivery(message, CancellationToken.None).ConfigureAwait(false);

            await _engine.AdvanceAsync().ConfigureAwait(false);
            DeliverySucceeded?.Invoke(message);
        }
        catch (Exception)
        {
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