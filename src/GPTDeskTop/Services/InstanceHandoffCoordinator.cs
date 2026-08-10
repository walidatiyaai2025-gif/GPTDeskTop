using System.IO.Pipes;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record InstanceHandoffState(
    string DatabasePath,
    AppConfig Config,
    IReadOnlyList<long> RunningMonitorIds);

public sealed record InstanceHandoffOffer(
    bool Accepted,
    string RequestId,
    int PreviousProcessId,
    string DatabasePath,
    AppConfig? Config,
    long[] RunningMonitorIds,
    string? Error = null);

public sealed record InstanceHandoffStartupResult(
    InstanceHandoffCoordinator? Coordinator,
    InstanceHandoffOffer? Takeover,
    string? Error)
{
    public bool IsPrimary => Coordinator is not null && string.IsNullOrWhiteSpace(Error);
}

public sealed class InstanceHandoffCoordinator : IDisposable
{
    private const string MutexName = @"Local\GPTDeskTop.SingleInstance.v1";
    private const string PipeName = "GPTDeskTop.InstanceHandoff.v1";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan OwnershipTimeout = TimeSpan.FromSeconds(25);

    private readonly Mutex _ownershipMutex;
    private readonly CancellationTokenSource _serverCancellation = new();
    private Task? _serverTask;
    private bool _ownsMutex;
    private int _takeoverCommitted;
    private bool _disposed;

    private InstanceHandoffCoordinator(Mutex ownershipMutex, bool ownsMutex)
    {
        _ownershipMutex = ownershipMutex;
        _ownsMutex = ownsMutex;
    }

    public static async Task<InstanceHandoffStartupResult> AcquireOrTakeOverAsync(
        CancellationToken cancellationToken = default)
    {
        Mutex? probeMutex = null;
        try
        {
            probeMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                return new InstanceHandoffStartupResult(
                    new InstanceHandoffCoordinator(probeMutex, ownsMutex: true),
                    null,
                    null);
            }

            probeMutex.Dispose();
            probeMutex = null;

            var offer = await RequestTakeoverAsync(cancellationToken).ConfigureAwait(false);
            if (offer is null)
            {
                return new InstanceHandoffStartupResult(
                    null,
                    null,
                    "Another GPTDeskTop instance is already running but did not accept a safe handoff request. The second runtime was not started.");
            }

            var takeoverMutex = new Mutex(initiallyOwned: false, MutexName);
            var acquired = false;
            try
            {
                acquired = await Task.Run(
                    () => WaitForMutex(takeoverMutex, OwnershipTimeout),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                takeoverMutex.Dispose();
                throw;
            }

            if (!acquired)
            {
                takeoverMutex.Dispose();
                return new InstanceHandoffStartupResult(
                    null,
                    offer,
                    "The previous GPTDeskTop instance accepted takeover but did not release runtime ownership before the safety timeout. The second runtime was not started.");
            }

            return new InstanceHandoffStartupResult(
                new InstanceHandoffCoordinator(takeoverMutex, ownsMutex: true),
                offer,
                null);
        }
        catch (OperationCanceledException)
        {
            probeMutex?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            probeMutex?.Dispose();
            return new InstanceHandoffStartupResult(null, null, ex.Message);
        }
    }

    public void StartTakeoverServer(
        Func<CancellationToken, Task<InstanceHandoffState>> stateFactory,
        Func<Task> committedTakeoverShutdown)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stateFactory);
        ArgumentNullException.ThrowIfNull(committedTakeoverShutdown);
        if (_serverTask is not null) throw new InvalidOperationException("The instance handoff server has already been started.");

        _serverTask = Task.Run(
            () => RunServerAsync(stateFactory, committedTakeoverShutdown, _serverCancellation.Token),
            CancellationToken.None);
    }

    public static async Task<int> ResumeRunningMonitorsAsync(
        InstanceHandoffOffer offer,
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitorService,
        LocalDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(database);

        var requestedIds = offer.RunningMonitorIds
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();
        if (requestedIds.Count == 0) return 0;

        List<ChromeTab>? tabs = null;
        Exception? lastChromeError = null;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                tabs = await chrome.GetTabsAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastChromeError = ex;
                if (attempt < 20)
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }

        if (tabs is null)
            throw new InvalidOperationException("Monitor Chrome did not become reachable during instance handoff resume.", lastChromeError);

        var savedMonitors = await database.GetSavedMonitorsAsync(cancellationToken).ConfigureAwait(false);
        var resumed = 0;

        foreach (var savedMonitor in savedMonitors.Where(saved => requestedIds.Contains(saved.Id)).OrderBy(saved => saved.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!savedMonitor.Enabled) continue;
            if (!RuntimeHealthPresentation.IsChatGptConversationUrl(savedMonitor.Url)) continue;

            var resolution = SavedMonitorTabResolver.Resolve(savedMonitor, tabs);
            var tab = resolution.Tab;
            if (tab is null) continue;

            try
            {
                await monitorService.StartMonitorAsync(savedMonitor, tab, cancellationToken).ConfigureAwait(false);
                if (monitorService.IsMonitorRunning(savedMonitor.Id)) resumed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ExceptionLogService.Log(ex, "InstanceHandoff.ResumeMonitor", savedMonitor.Id, savedMonitor.TabId, savedMonitor.Title);
            }
        }

        return resumed;
    }

    private async Task RunServerAsync(
        Func<CancellationToken, Task<InstanceHandoffState>> stateFactory,
        Func<Task> committedTakeoverShutdown,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                using var exchangeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                exchangeTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                var requestLine = await reader.ReadLineAsync(exchangeTimeout.Token).ConfigureAwait(false);
                var request = Deserialize<InstanceHandoffRequest>(requestLine);
                if (request is null || string.IsNullOrWhiteSpace(request.RequestId))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffOffer(
                        false,
                        string.Empty,
                        Environment.ProcessId,
                        string.Empty,
                        null,
                        Array.Empty<long>(),
                        "Invalid takeover request."))).ConfigureAwait(false);
                    continue;
                }

                if (Interlocked.CompareExchange(ref _takeoverCommitted, 0, 0) != 0)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffOffer(
                        false,
                        request.RequestId,
                        Environment.ProcessId,
                        string.Empty,
                        null,
                        Array.Empty<long>(),
                        "A takeover is already in progress."))).ConfigureAwait(false);
                    continue;
                }

                var state = await stateFactory(exchangeTimeout.Token).ConfigureAwait(false);
                var offer = new InstanceHandoffOffer(
                    true,
                    request.RequestId,
                    Environment.ProcessId,
                    state.DatabasePath,
                    state.Config,
                    state.RunningMonitorIds.Where(id => id > 0).Distinct().ToArray());

                await writer.WriteLineAsync(JsonSerializer.Serialize(offer)).ConfigureAwait(false);

                var ackLine = await reader.ReadLineAsync(exchangeTimeout.Token).ConfigureAwait(false);
                var ack = Deserialize<InstanceHandoffAck>(ackLine);
                if (ack is null || !string.Equals(ack.RequestId, request.RequestId, StringComparison.Ordinal))
                    continue;

                if (Interlocked.CompareExchange(ref _takeoverCommitted, 1, 0) != 0)
                    continue;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await committedTakeoverShutdown().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ExceptionLogService.Log(ex, "InstanceHandoff.CommittedShutdown");
                        Environment.Exit(2);
                    }
                });

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "InstanceHandoff.Server");
                try { await Task.Delay(250, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static async Task<InstanceHandoffOffer?> RequestTakeoverAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ConnectTimeout;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                var remaining = deadline - DateTimeOffset.UtcNow;
                var connectMilliseconds = (int)Math.Clamp(remaining.TotalMilliseconds, 100, 1000);
                await pipe.ConnectAsync(connectMilliseconds, cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var request = new InstanceHandoffRequest(Guid.NewGuid().ToString("N"), Environment.ProcessId);
                await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);

                using var exchangeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                exchangeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var offerLine = await reader.ReadLineAsync(exchangeTimeout.Token).ConfigureAwait(false);
                var offer = Deserialize<InstanceHandoffOffer>(offerLine);
                if (offer is null || !offer.Accepted || !string.Equals(offer.RequestId, request.RequestId, StringComparison.Ordinal))
                    return null;

                await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffAck(request.RequestId))).ConfigureAwait(false);
                return offer;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        if (lastError is not null)
            ExceptionLogService.Log(lastError, "InstanceHandoff.ClientConnect");
        return null;
    }

    private static bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serverCancellation.Cancel();

        if (_ownsMutex)
        {
            try { _ownershipMutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _ownsMutex = false;
        }

        _ownershipMutex.Dispose();
        _serverCancellation.Dispose();
    }

    private sealed record InstanceHandoffRequest(string RequestId, int ProcessId);
    private sealed record InstanceHandoffAck(string RequestId);
}