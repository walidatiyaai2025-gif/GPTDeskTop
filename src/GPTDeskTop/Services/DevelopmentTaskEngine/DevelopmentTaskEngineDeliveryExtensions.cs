namespace GPTDeskTop.Services.DevelopmentTaskEngine;

public static class DevelopmentTaskEngineDeliveryExtensions
{
    /// <summary>
    /// Persists the verified delivery receipt before the engine advances.
    /// The receipt is deliberately written through the engine checkpoint so
    /// restart can recover the same monitor/tab/message identity.
    /// </summary>
    public static async Task CheckpointDeliveredAsync(
        this DevelopmentTaskEngine engine,
        string monitorId,
        string tabId,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (string.IsNullOrWhiteSpace(monitorId)) throw new ArgumentException("Monitor ID is required.", nameof(monitorId));
        if (string.IsNullOrWhiteSpace(tabId)) throw new ArgumentException("Tab ID is required.", nameof(tabId));
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentException("Delivery fingerprint is required.", nameof(fingerprint));

        engine.State.LastMonitorId = monitorId;
        engine.State.LastTabId = tabId;
        engine.State.LastDeliveredMessageIndex = engine.State.CurrentMessageIndex;
        engine.State.LastDeliveredMessageFingerprint = fingerprint;
        await engine.CheckpointAsync(monitorId, tabId, cancellationToken).ConfigureAwait(false);
    }
}
