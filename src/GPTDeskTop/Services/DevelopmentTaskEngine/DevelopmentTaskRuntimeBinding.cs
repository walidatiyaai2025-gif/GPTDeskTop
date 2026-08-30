namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Production binding for the development-plan engine: one lifecycle, dynamic
/// saved-monitor/chat delivery, and stable assistant-response observation.
/// Delivery and response observers are attached before Start/Resume so the first
/// prompt cannot bypass either half of the workflow.
/// </summary>
public sealed class DevelopmentTaskRuntimeBinding : IAsyncDisposable
{
    private readonly DevelopmentTaskRuntimeCoordinator _runtime;
    private readonly DevelopmentTaskDynamicDeliveryCoordinator _delivery;
    private readonly DevelopmentTaskResponseWatcher _responses;
    private readonly DevelopmentTaskMonitorTargetFactory _targetFactory;
    private bool _disposed;

    public DevelopmentTaskRuntimeBinding(
        DevelopmentTaskEngine engine,
        DevelopmentTaskMonitorTargetFactory targetFactory)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(targetFactory);

        Engine = engine;
        _targetFactory = targetFactory;
        _runtime = new DevelopmentTaskRuntimeCoordinator(engine);
        _delivery = new DevelopmentTaskDynamicDeliveryCoordinator(engine, targetFactory);
        _responses = targetFactory.CreateResponseWatcher(engine);
    }

    public DevelopmentTaskEngine Engine { get; }
    public DevelopmentTaskState State => Engine.State;
    public bool IsStarted => _runtime.IsStarted;

    public async Task<bool> StartAsync(
        string planId,
        string planTitle,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Resolve ownership before changing plan state. This proves that the per-monitor
        // Development Messages checkbox was persisted, the conversation is live, and the
        // canonical target can be rebound. ResolveEnabledRecipientsAsync also retires the
        // legacy single-AutoReply worker, guaranteeing one continuation owner before prompt #1.
        var recipients = await _targetFactory.ResolveEnabledRecipientsAsync(cancellationToken).ConfigureAwait(false);
        if (recipients.Count == 0)
        {
            throw new InvalidOperationException(
                "Development Messages cannot start because no eligible monitor is opted in. " +
                "Open Saved Monitors -> Edit Monitor, enable 'Use Development Messages as monitor auto-reply plan', " +
                "keep that ChatGPT conversation open, then press Start again.");
        }

        return await _runtime.StartAsync(planId, planTitle, cancellationToken).ConfigureAwait(false);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime.PauseAsync(cancellationToken);
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime.ResumeAsync(cancellationToken);
    }

    public Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime.ResumeIfActiveAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _delivery.DisposeAsync().ConfigureAwait(false);
        await _responses.DisposeAsync().ConfigureAwait(false);
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }
}
