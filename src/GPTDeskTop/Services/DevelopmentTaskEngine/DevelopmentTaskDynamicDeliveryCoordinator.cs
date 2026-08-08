namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Resolves live recipients for every development-plan message instead of keeping
/// stale ChromeTab objects across Cooling or process restart. This is the runtime
/// integration point between the persisted monitor registry and the multi-monitor
/// delivery coordinator.
/// </summary>
public sealed class DevelopmentTaskDynamicDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly DevelopmentTaskMonitorTargetFactory _targetFactory;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private bool _disposed;

    public event EventHandler<string>? DeliverySucceeded;
    public event EventHandler<string>? DeliveryFailed;

    public DevelopmentTaskDynamicDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        DevelopmentTaskMonitorTargetFactory targetFactory)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        _engine.MessageReady += OnMessageReady;
    }

    private void OnMessageReady(object? sender, string message) => _ = DeliverAsync(message);

    private async Task DeliverAsync(string message)
    {
        if (_disposed) return;
        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            var recipients = await _targetFactory.ResolveEnabledRecipientsAsync().ConfigureAwait(false);
            if (recipients.Count == 0)
            {
                DeliveryFailed?.Invoke(this, message);
                return;
            }

            await using var coordinator = new DevelopmentTaskMultiMonitorDeliveryCoordinator(_engine, recipients);
            var succeeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.DeliverySucceeded += (_, _) => succeeded.TrySetResult(true);
            coordinator.DeliveryFailed += (_, _) => succeeded.TrySetResult(false);

            // The coordinator subscribes during construction. Triggering delivery is
            // deliberately done through the same engine event path, so no second copy
            // of the message-generation logic exists here.
            await coordinator.DeliverCurrentMessageAsync(message).ConfigureAwait(false);
            var result = await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (result) DeliverySucceeded?.Invoke(this, message);
            else DeliveryFailed?.Invoke(this, message);
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
