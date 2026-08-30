namespace GPTDeskTop.Services.DevelopmentTaskEngine;

/// <summary>
/// Owns one development-task engine lifecycle. Explicit Start always creates a fresh
/// plan run from prompt #1; Resume is the only path that restores persisted position.
/// The coordinator keeps its active flag synchronized with pause/resume/stop so a
/// previous lifecycle state can never make the Start button appear to do nothing.
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

            // Start is an explicit operator command, not crash recovery. A stale Stopped,
            // Paused, Faulted or Completed checkpoint must never suppress prompt #1.
            if (_started && _engine.State.Status is DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling)
                return false;

            await _engine.StartAsync(planId, planTitle, cancellationToken).ConfigureAwait(false);
            _started = _engine.State.Status is DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling;
            return _started;
        }
        finally { _gate.Release(); }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _engine.PauseAsync(cancellationToken).ConfigureAwait(false);
            _started = false;
        }
        finally { _gate.Release(); }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _engine.ResumeAsync(cancellationToken).ConfigureAwait(false);
            _started = _engine.State.Status is DevelopmentTaskEngineStatus.Working or DevelopmentTaskEngineStatus.Cooling;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ResumeIfActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return false;
            _started = await _engine.ResumeIfActiveAsync(cancellationToken).ConfigureAwait(false);
            return _started;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Stop is authoritative even after Pause or a recovered state where the local
            // coordinator flag is false.
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
