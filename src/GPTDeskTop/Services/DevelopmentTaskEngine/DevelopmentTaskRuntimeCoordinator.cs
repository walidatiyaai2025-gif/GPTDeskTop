namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Owns one development-task engine lifecycle. Repeated Start calls are idempotent,
/// while Stop cancels the single active worker. Recovery is delegated to the persisted state.
/// </summary>
public sealed class DevelopmentTaskRuntimeCoordinator : IAsyncDisposable
{
    private readonly DevelopmentTaskEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _disposed;

    public DevelopmentTaskRuntimeCoordinator(DevelopmentTaskEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public bool IsStarted => _started;

    public async Task<bool> StartAsync(string planId, string planTitle, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return false;

            await _engine.ResumeAsync(cancellationToken).ConfigureAwait(false);
            if (_engine.State.Status == DevelopmentTaskEngineStatus.Completed)
            {
                _started = false;
                return false;
            }

            // Resume restores persisted position; for a new plan, initialize it explicitly.
            if (!string.Equals(_engine.State.PlanId, planId, StringComparison.Ordinal) ||
                _engine.State.TotalMessages == 0)
            {
                await _engine.StartAsync(planId, planTitle, cancellationToken).ConfigureAwait(false);
            }

            _started = true;
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started) return;
            await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            _started = false;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _engine.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
