namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Delivers each development-plan message to every recovered monitor/chat.
/// A message advances only after every recipient has a verified receipt.
/// Successful recipients are persisted so a later retry never sends them again.
/// </summary>
public sealed class DevelopmentTaskMultiMonitorDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly IReadOnlyList<DevelopmentTaskMonitorRecipient> _recipients;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private bool _disposed;

    public event EventHandler<string>? DeliverySucceeded;
    public event EventHandler<string>? DeliveryFailed;

    public DevelopmentTaskMultiMonitorDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        IEnumerable<DevelopmentTaskMonitorRecipient> recipients)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _recipients = recipients?.Where(x => x is not null).ToArray()
            ?? throw new ArgumentNullException(nameof(recipients));
        if (_recipients.Count == 0) throw new ArgumentException("At least one monitor recipient is required.", nameof(recipients));
        _engine.MessageReady += OnMessageReady;
    }

    private void OnMessageReady(object? sender, string message) => _ = DeliverAsync(message);

    public Task DeliverCurrentMessageAsync(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DeliverAsync(message);
    }

    private async Task DeliverAsync(string message)
    {
        if (_disposed) return;
        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            var messageIndex = _engine.State.CurrentMessageIndex;
            var fingerprint = DevelopmentTaskDeliveryCoordinator.Fingerprint(message);
            var allDelivered = true;

            foreach (var recipient in _recipients)
            {
                if (_engine.State.DeliveryReceipts.TryGetValue(recipient.MonitorId, out var receipt) &&
                    receipt.MessageIndex == messageIndex &&
                    string.Equals(receipt.TabId, recipient.TabId, StringComparison.Ordinal) &&
                    string.Equals(receipt.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                var sent = await recipient.SendVerifiedAsync(message).ConfigureAwait(false);
                if (!sent)
                {
                    allDelivered = false;
                    continue;
                }

                _engine.State.DeliveryReceipts[recipient.MonitorId] = new DevelopmentTaskDeliveryReceipt
                {
                    MonitorId = recipient.MonitorId,
                    TabId = recipient.TabId,
                    MessageIndex = messageIndex,
                    Fingerprint = fingerprint,
                    DeliveredAt = DateTimeOffset.UtcNow,
                    Revision = _engine.State.Revision + 1
                };
                _engine.State.LastMonitorId = recipient.MonitorId;
                _engine.State.LastTabId = recipient.TabId;
                _engine.State.LastDeliveredMessageIndex = messageIndex;
                _engine.State.LastDeliveredMessageFingerprint = fingerprint;
                await _engine.CheckpointAsync(recipient.MonitorId, recipient.TabId).ConfigureAwait(false);
            }

            if (!allDelivered)
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

public sealed record DevelopmentTaskMonitorRecipient(
    string MonitorId,
    string TabId,
    Func<string, Task<bool>> SendVerifiedAsync);
