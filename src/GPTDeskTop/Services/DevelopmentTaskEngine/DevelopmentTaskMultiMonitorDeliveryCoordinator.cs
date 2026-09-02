using GPTDeskTop.Models;

namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Delivers each development-plan message to every recovered monitor/chat.
/// Verified outbound delivery is persisted per recipient, but delivery alone never
/// advances the plan. Once every recipient has a receipt the engine waits for a
/// stable assistant response from every expected monitor.
/// </summary>
public sealed class DevelopmentTaskMultiMonitorDeliveryCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly IReadOnlyList<DevelopmentTaskMonitorRecipient> _recipients;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private readonly bool _subscribedToEngine;
    private bool _disposed;

    public event Action<string>? DeliverySucceeded;
    public event Action<string>? DeliveryFailed;

    public DevelopmentTaskMultiMonitorDeliveryCoordinator(
        DevelopmentTaskEngine engine,
        IEnumerable<DevelopmentTaskMonitorRecipient> recipients,
        bool subscribeToEngine = true)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _recipients = recipients?.Where(x => x is not null).ToArray()
            ?? throw new ArgumentNullException(nameof(recipients));
        if (_recipients.Count == 0) throw new ArgumentException("At least one monitor recipient is required.", nameof(recipients));
        _subscribedToEngine = subscribeToEngine;
        if (_subscribedToEngine) _engine.MessageReady += OnMessageReady;
    }

    private void OnMessageReady(string message) => _ = DeliverAsync(message);

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

                ChatPageState before;
                try
                {
                    before = await recipient.ReadStateAsync().ConfigureAwait(false);
                }
                catch
                {
                    allDelivered = false;
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
                    AssistantCountBeforeDelivery = before.AssistantCount,
                    AssistantFingerprintBeforeDelivery = DevelopmentTaskDeliveryCoordinator.Fingerprint(before.LastAssistantText ?? string.Empty),
                    DeliveredAt = DateTimeOffset.UtcNow,
                    Revision = _engine.State.Revision + 1
                };
                await _engine.CheckpointDeliveredAsync(recipient.MonitorId, recipient.TabId, fingerprint).ConfigureAwait(false);
            }

            if (!allDelivered)
            {
                await _engine.ReportDeliveryFailureAsync(
                    $"Development message #{messageIndex + 1} was not verified for every eligible monitor. Successful receipts are preserved and only missing recipients will be retried.").ConfigureAwait(false);
                DeliveryFailed?.Invoke(message);
                return;
            }

            await _engine.MarkAwaitingAssistantResponseAsync(
                _recipients.Select(recipient => recipient.MonitorId)).ConfigureAwait(false);
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

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_subscribedToEngine) _engine.MessageReady -= OnMessageReady;
        _deliveryGate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record DevelopmentTaskMonitorRecipient(
    string MonitorId,
    string TabId,
    Func<string, Task<bool>> SendVerifiedAsync,
    Func<Task<ChatPageState>> ReadStateAsync)
{
    public DevelopmentTaskMonitorRecipient(
        string monitorId,
        string tabId,
        Func<string, Task<bool>> sendVerifiedAsync)
        : this(
            monitorId,
            tabId,
            sendVerifiedAsync,
            () => Task.FromResult(new ChatPageState(0, string.Empty, false, string.Empty)))
    {
    }
}
