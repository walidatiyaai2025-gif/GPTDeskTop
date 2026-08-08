namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Bridges DevelopmentTaskEngine message emission to a verified chat sender.
/// A task advances only after the sender confirms that the message was accepted.
/// </summary>
public sealed class DevelopmentTaskDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly Func<string, CancellationToken, Task<bool>> _verifiedSender;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private bool _disposed;

    public event EventHandler<string>? DeliverySucceeded;
    public event EventHandler<string>? DeliveryFailed;

    public DevelopmentTaskDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        Func<string, CancellationToken, Task<bool>> verifiedSender)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _verifiedSender = verifiedSender ?? throw new ArgumentNullException(nameof(verifiedSender));
        _engine.MessageReady += OnMessageReady;
    }

    private void OnMessageReady(object? sender, string message)
    {
        _ = DeliverAsync(message);
    }

    private async Task DeliverAsync(string message)
    {
        if (_disposed) return;
        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var sent = await _verifiedSender(message, CancellationToken.None).ConfigureAwait(false);
            if (!sent)
            {
                DeliveryFailed?.Invoke(this, message);
                return;
            }

            await _engine.AdvanceAsync().ConfigureAwait(false);
            DeliverySucceeded?.Invoke(this, message);
        }
        catch (Exception)
        {
            DeliveryFailed?.Invoke(this, message);
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _engine.MessageReady -= OnMessageReady;
        _deliveryGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
