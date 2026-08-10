using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services.DevelopmentTaskEngine;

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
    private static readonly TimeSpan OrphanedOwnerTimeout = TimeSpan.FromSeconds(2);
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

    public static InstanceHandoffStartupResult AcquireOrTakeOver()
    {
        Mutex? probeMutex = null;
        Mutex? takeoverMutex = null;
        try
        {
            probeMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                var coordinator = new InstanceHandoffCoordinator(probeMutex, ownsMutex: true);
                probeMutex = null;
                return new InstanceHandoffStartupResult(coordinator, null, null);
            }

            probeMutex.Dispose();
            probeMutex = null;

            // Open the exact ownership primitive before declaring this replacement ready.
            // Once Ready is sent, no subsequent local handle/config preflight is allowed to
            // become a reason for the outgoing runtime to have exited unnecessarily.
            takeoverMutex = new Mutex(initiallyOwned: false, MutexName);
            var offer = RequestTakeover(takeoverMutex);
            if (offer is null)
            {
                // Preserve the fatal-restart path only when no commit-ready message was sent.
                // If the prior process disappeared before negotiation, a released/abandoned
                // mutex can safely be claimed. An uncertain post-Ready outcome throws instead
                // and never reaches this fallback.
                if (WaitForMutex(takeoverMutex, OrphanedOwnerTimeout))
                {
                    var coordinator = new InstanceHandoffCoordinator(takeoverMutex, ownsMutex: true);
                    takeoverMutex = null;
                    return new InstanceHandoffStartupResult(coordinator, null, null);
                }

                takeoverMutex.Dispose();
                takeoverMutex = null;
                return new InstanceHandoffStartupResult(
                    null,
                    null,
                    "Another GPTDeskTop instance is already running but did not accept a safe handoff request. The second runtime was not started.");
            }

            var acquired = WaitForMutex(takeoverMutex, OwnershipTimeout);
            if (!acquired)
            {
                takeoverMutex.Dispose();
                takeoverMutex = null;
                return new InstanceHandoffStartupResult(
                    null,
                    offer,
                    "The previous GPTDeskTop instance committed takeover but did not release runtime ownership before the safety timeout. The second runtime was not started.");
            }

            var takeoverCoordinator = new InstanceHandoffCoordinator(takeoverMutex, ownsMutex: true);
            takeoverMutex = null;
            return new InstanceHandoffStartupResult(takeoverCoordinator, offer, null);
        }
        catch (Exception ex)
        {
            probeMutex?.Dispose();
            takeoverMutex?.Dispose();
            return new InstanceHandoffStartupResult(null, null, ex.Message);
        }
    }

    public void StartTakeoverServer(
        Func<CancellationToken, Task<InstanceHandoffState>> stateFactory,
        Func<Task> committedTakeoverShutdown)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InstanceHandoffCoordinator));
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
                await monitorService.StartMonitorAsync(savedMonitor, tab).ConfigureAwait(false);
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
                if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || request.ProcessId <= 0)
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

                var readyLine = await reader.ReadLineAsync(exchangeTimeout.Token).ConfigureAwait(false);
                var ready = Deserialize<InstanceHandoffReady>(readyLine);
                if (ready is null ||
                    !string.Equals(ready.RequestId, request.RequestId, StringComparison.Ordinal) ||
                    ready.ProcessId != request.ProcessId)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffCommit(
                        request.RequestId,
                        false,
                        "Replacement readiness did not match the takeover request."))).ConfigureAwait(false);
                    continue;
                }

                if (!IsProcessAlive(request.ProcessId))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffCommit(
                        request.RequestId,
                        false,
                        "Replacement process exited before takeover commit."))).ConfigureAwait(false);
                    continue;
                }

                if (Interlocked.CompareExchange(ref _takeoverCommitted, 1, 0) != 0)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffCommit(
                        request.RequestId,
                        false,
                        "A takeover is already in progress."))).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    // Commit acceptance is written before shutdown starts. If the write itself
                    // fails, reset the commit marker and keep this runtime active.
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstanceHandoffCommit(
                        request.RequestId,
                        true))).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Exchange(ref _takeoverCommitted, 0);
                    throw;
                }

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

    private static InstanceHandoffOffer? RequestTakeover(Mutex takeoverMutex)
    {
        var deadline = DateTimeOffset.UtcNow + ConnectTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var readySent = false;
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.None);

                var remaining = deadline - DateTimeOffset.UtcNow;
                var connectMilliseconds = (int)Math.Clamp(remaining.TotalMilliseconds, 100, 1000);
                pipe.Connect(connectMilliseconds);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var request = new InstanceHandoffRequest(Guid.NewGuid().ToString("N"), Environment.ProcessId);
                writer.WriteLine(JsonSerializer.Serialize(request));

                var offerReadTask = reader.ReadLineAsync();
                if (!offerReadTask.Wait(TimeSpan.FromSeconds(10))) return null;
                var offer = Deserialize<InstanceHandoffOffer>(offerReadTask.Result);
                if (offer is null || !offer.Accepted || !string.Equals(offer.RequestId, request.RequestId, StringComparison.Ordinal))
                    return null;

                var preflightError = ValidateOfferForCommit(offer, takeoverMutex);
                if (preflightError is not null)
                    return null;

                writer.WriteLine(JsonSerializer.Serialize(new InstanceHandoffReady(request.RequestId, Environment.ProcessId)));
                readySent = true;

                var commitReadTask = reader.ReadLineAsync();
                if (!commitReadTask.Wait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Takeover readiness was sent but commit acceptance was not received before timeout.");

                var commit = Deserialize<InstanceHandoffCommit>(commitReadTask.Result);
                if (commit is null || !string.Equals(commit.RequestId, request.RequestId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Takeover readiness was sent but the commit response was invalid or uncorrelated.");
                if (!commit.Accepted)
                    return null;

                return offer;
            }
            catch (Exception ex)
            {
                if (readySent)
                    throw new InvalidOperationException(
                        "The replacement declared takeover readiness but could not confirm whether the previous runtime committed shutdown. The second runtime was not started.",
                        ex);
                Thread.Sleep(200);
            }
        }

        return null;
    }

    private static string? ValidateOfferForCommit(InstanceHandoffOffer offer, Mutex takeoverMutex)
    {
        if (takeoverMutex.SafeWaitHandle.IsClosed || takeoverMutex.SafeWaitHandle.IsInvalid)
            return "The replacement could not open the shared runtime ownership mutex.";
        if (offer.Config is null)
            return "The takeover offer did not contain effective application configuration.";
        if (string.IsNullOrWhiteSpace(offer.DatabasePath) || !Path.IsPathFullyQualified(offer.DatabasePath))
            return "The takeover offer did not contain an absolute database path.";
        if (!File.Exists(offer.DatabasePath))
            return "The takeover database does not exist at the offered absolute path.";
        if (!Uri.TryCreate(offer.Config.Chrome.DebuggingBaseUrl, UriKind.Absolute, out var debuggingUri) ||
            (debuggingUri.Scheme != Uri.UriSchemeHttp && debuggingUri.Scheme != Uri.UriSchemeHttps))
            return "The takeover offer contains an invalid Chrome debugging base URL.";
        if (offer.Config.Chrome.DebuggingPort is <= 0 or > 65535)
            return "The takeover offer contains an invalid Chrome debugging port.";
        if (offer.Config.Monitoring.PollIntervalMilliseconds <= 0 || offer.Config.Monitoring.StableResponseMilliseconds <= 0)
            return "The takeover offer contains invalid monitoring timing configuration.";
        if (!IsProcessAlive(offer.PreviousProcessId))
            return "The previous runtime exited before the replacement completed takeover preflight.";
        return null;
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
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
    private sealed record InstanceHandoffReady(string RequestId, int ProcessId);
    private sealed record InstanceHandoffCommit(string RequestId, bool Accepted, string? Error = null);
}
