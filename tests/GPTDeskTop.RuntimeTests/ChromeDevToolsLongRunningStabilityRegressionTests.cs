using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeDevToolsLongRunningStabilityRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void CdpCommandPathHasIndependentBoundedTimeoutAndPreservesCallerCancellation()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains("CommandTimeout = TimeSpan.FromSeconds(12)", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("commandCts.CancelAfter(CommandTimeout)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpReceiveBufferIsPooledAndMessageGrowthIsBounded()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains("ArrayPool<byte>.Shared.Rent(ReceiveBufferSize)", source, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Return(buffer)", source, StringComparison.Ordinal);
        Assert.Contains("MaxDevToolsMessageBytes = 2 * 1024 * 1024", source, StringComparison.Ordinal);
        Assert.Contains("stream.Length + result.Count > MaxDevToolsMessageBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new byte[64 * 1024]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpSessionsAreReusedPerTargetAndBrokenTargetsAreRecreated()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains("Dictionary<string, DevToolsSession> _sessions", source, StringComparison.Ordinal);
        Assert.Contains("existing.Matches(tab.WebSocketDebuggerUrl) && existing.IsUsable", source, StringComparison.Ordinal);
        Assert.Contains("session = new DevToolsSession(tab.WebSocketDebuggerUrl)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly ClientWebSocket _socket = new();", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _commandGate = new(1, 1);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using var socket = new ClientWebSocket();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChromeServicePrunesAndInvalidatesTargetSessions()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("_sessionPool.Prune(tabs)", source, StringComparison.Ordinal);
        Assert.Contains("finally { _sessionPool.Invalidate(tab.Id); }", source, StringComparison.Ordinal);
        Assert.Contains("finally { _sessionPool.Clear(); }", source, StringComparison.Ordinal);
        Assert.Contains("=> _sessionPool.SendCommandAsync(tab, method, parameters, cancellationToken, extractRuntimeValue);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpSessionRetirementDisposesSocketOnlyAfterExclusiveGateReacquisition()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains("private int _retired;", source, StringComparison.Ordinal);
        Assert.Contains("private int _socketDisposed;", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _retired, 1)", source, StringComparison.Ordinal);
        Assert.Contains("TryDisposeSocketIfRetired();", source, StringComparison.Ordinal);
        Assert.Contains("if (!_commandGate.Wait(0)) return;", source, StringComparison.Ordinal);
        Assert.Contains("private void DisposeSocketUnderGate()", source, StringComparison.Ordinal);

        var sendCommand = source.IndexOf(
            "public async Task<JsonElement> SendCommandAsync(",
            StringComparison.Ordinal);
        var ioCatch = source.IndexOf(
            "catch (IOException)",
            sendCommand,
            StringComparison.Ordinal);
        var releaseAfterCommand = source.IndexOf(
            "_commandGate.Release();",
            ioCatch,
            StringComparison.Ordinal);
        var cleanupAfterRelease = source.IndexOf(
            "TryDisposeSocketIfRetired();",
            releaseAfterCommand,
            StringComparison.Ordinal);
        Assert.True(
            sendCommand >= 0
            && ioCatch > sendCommand
            && releaseAfterCommand > ioCatch
            && cleanupAfterRelease > releaseAfterCommand);

        var cleanupHelper = source.IndexOf("private void TryDisposeSocketIfRetired()", StringComparison.Ordinal);
        var cleanupGateAcquire = source.IndexOf("if (!_commandGate.Wait(0)) return;", cleanupHelper, StringComparison.Ordinal);
        var guardedDispose = source.IndexOf("DisposeSocketUnderGate();", cleanupGateAcquire, StringComparison.Ordinal);
        Assert.True(cleanupHelper >= 0 && cleanupGateAcquire > cleanupHelper && guardedDispose > cleanupGateAcquire);

        Assert.Equal(
            1,
            source.Split("_socket.Dispose()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DisposedSocketRaceIsNormalizedToRecoverableIoFailure()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains("catch (ObjectDisposedException ex)", source, StringComparison.Ordinal);
        Assert.Contains(
            "throw new IOException(\"Chrome DevTools session became unavailable during command execution.\", ex);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("catch (ObjectDisposedException)", source, StringComparison.Ordinal);

        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, [new IOException("retired CDP socket")])!);
    }

    [Fact]
    public void CdpCommandTimeoutRemainsARecoverableMonitorFailure()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, [new TimeoutException("CDP command timed out")])!);
    }
}
