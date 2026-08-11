using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

internal sealed class ChromeDevToolsSessionPool : IDisposable
{
    internal const int ReceiveBufferSize = 64 * 1024;
    internal const int MaxDevToolsMessageBytes = 2 * 1024 * 1024;
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(12);

    private readonly object _sync = new();
    private readonly Dictionary<string, DevToolsSession> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;

    public Task<JsonElement> SendCommandAsync(
        ChromeTab tab,
        string method,
        object parameters,
        CancellationToken cancellationToken,
        bool extractRuntimeValue = false)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (string.IsNullOrWhiteSpace(tab.Id))
            throw new InvalidOperationException("The selected tab does not expose a Chrome target ID.");
        if (string.IsNullOrWhiteSpace(tab.WebSocketDebuggerUrl))
            throw new InvalidOperationException("The selected tab does not expose a DevTools WebSocket URL.");

        var session = GetOrCreateSession(tab);
        return session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue);
    }

    public void Prune(IReadOnlyCollection<ChromeTab> liveTabs)
    {
        ArgumentNullException.ThrowIfNull(liveTabs);
        var liveIds = liveTabs
            .Select(tab => tab.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        List<DevToolsSession>? stale = null;
        lock (_sync)
        {
            if (_disposed) return;
            foreach (var pair in _sessions.ToArray())
            {
                if (liveIds.Contains(pair.Key)) continue;
                _sessions.Remove(pair.Key);
                (stale ??= []).Add(pair.Value);
            }
        }

        DisposeSessions(stale);
    }

    public void Invalidate(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        DevToolsSession? stale = null;
        lock (_sync)
        {
            if (_sessions.Remove(targetId, out var session)) stale = session;
        }
        stale?.Dispose();
    }

    public void Clear()
    {
        List<DevToolsSession> sessions;
        lock (_sync)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }
        DisposeSessions(sessions);
    }

    public void Dispose()
    {
        List<DevToolsSession> sessions;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }
        DisposeSessions(sessions);
    }

    private DevToolsSession GetOrCreateSession(ChromeTab tab)
    {
        DevToolsSession? stale = null;
        DevToolsSession session;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.TryGetValue(tab.Id, out var existing))
            {
                if (existing.Matches(tab.WebSocketDebuggerUrl) && existing.IsUsable)
                    return existing;

                _sessions.Remove(tab.Id);
                stale = existing;
            }

            session = new DevToolsSession(tab.WebSocketDebuggerUrl);
            _sessions.Add(tab.Id, session);
        }

        stale?.Dispose();
        return session;
    }

    private static void DisposeSessions(IEnumerable<DevToolsSession>? sessions)
    {
        if (sessions is null) return;
        foreach (var session in sessions) session.Dispose();
    }

    private sealed class DevToolsSession : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly SemaphoreSlim _commandGate = new(1, 1);
        private int _nextCommandId;
        private int _broken;
        private int _retired;
        private int _socketDisposed;

        public DevToolsSession(string webSocketDebuggerUrl)
        {
            WebSocketDebuggerUrl = webSocketDebuggerUrl;
        }

        public string WebSocketDebuggerUrl { get; }

        public bool IsUsable
        {
            get
            {
                if (Volatile.Read(ref _retired) != 0
                    || Volatile.Read(ref _broken) != 0
                    || Volatile.Read(ref _socketDisposed) != 0)
                    return false;

                try
                {
                    var state = _socket.State;
                    return Volatile.Read(ref _retired) == 0
                           && Volatile.Read(ref _socketDisposed) == 0
                           && (state == WebSocketState.None || state == WebSocketState.Open);
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public bool Matches(string webSocketDebuggerUrl)
            => string.Equals(WebSocketDebuggerUrl, webSocketDebuggerUrl, StringComparison.Ordinal);

        public async Task<JsonElement> SendCommandAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken,
            bool extractRuntimeValue)
        {
            await _commandGate.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref _retired) != 0 || Volatile.Read(ref _broken) != 0)
                    throw new IOException("Chrome DevTools session was invalidated before the command could run.");

                using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                commandCts.CancelAfter(CommandTimeout);
                var commandToken = commandCts.Token;

                try
                {
                    if (_socket.State == WebSocketState.None)
                        await _socket.ConnectAsync(new Uri(WebSocketDebuggerUrl), commandToken);
                    if (_socket.State != WebSocketState.Open)
                        throw new IOException($"Chrome DevTools session is not open (state: {_socket.State}).");

                    var commandId = Interlocked.Increment(ref _nextCommandId);
                    var request = JsonSerializer.Serialize(new { id = commandId, method, @params = parameters });
                    var bytes = Encoding.UTF8.GetBytes(request);
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, commandToken);

                    var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
                    try
                    {
                        using var stream = new MemoryStream();
                        while (true)
                        {
                            var result = await _socket.ReceiveAsync(
                                new ArraySegment<byte>(buffer, 0, ReceiveBufferSize),
                                commandToken);
                            if (result.MessageType == WebSocketMessageType.Close)
                                throw new IOException("Chrome closed the DevTools connection.");

                            if (stream.Length + result.Count > MaxDevToolsMessageBytes)
                                throw new IOException($"Chrome DevTools message exceeded the {MaxDevToolsMessageBytes} byte safety limit.");

                            stream.Write(buffer, 0, result.Count);
                            if (!result.EndOfMessage) continue;

                            var payload = Encoding.UTF8.GetString(
                                stream.GetBuffer(),
                                0,
                                checked((int)stream.Length));
                            stream.SetLength(0);

                            JsonElement root;
                            try
                            {
                                using var document = JsonDocument.Parse(payload);
                                root = document.RootElement.Clone();
                            }
                            catch (JsonException ex)
                            {
                                throw new IOException("Chrome DevTools returned an invalid JSON payload.", ex);
                            }

                            if (!root.TryGetProperty("id", out var id) || id.GetInt32() != commandId)
                                continue;
                            if (root.TryGetProperty("error", out var error))
                                throw new InvalidOperationException($"Chrome DevTools error: {error}");
                            if (!extractRuntimeValue)
                                return root.TryGetProperty("result", out var commandResult)
                                    ? commandResult.Clone()
                                    : JsonDocument.Parse("null").RootElement.Clone();

                            var resultElement = root.GetProperty("result").GetProperty("result");
                            if (resultElement.TryGetProperty("subtype", out var subtype)
                                && subtype.GetString() == "error")
                            {
                                throw new InvalidOperationException(
                                    resultElement.TryGetProperty("description", out var description)
                                        ? description.GetString()
                                        : "JavaScript evaluation failed.");
                            }

                            return resultElement.TryGetProperty("value", out var value)
                                ? value.Clone()
                                : JsonDocument.Parse("null").RootElement.Clone();
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    MarkBroken();
                    throw new TimeoutException(
                        $"Chrome DevTools command '{method}' timed out after {CommandTimeout.TotalSeconds:0} seconds.");
                }
                catch (OperationCanceledException)
                {
                    MarkBroken();
                    throw;
                }
                catch (ObjectDisposedException ex)
                {
                    MarkBroken();
                    throw new IOException("Chrome DevTools session became unavailable during command execution.", ex);
                }
                catch (WebSocketException)
                {
                    MarkBroken();
                    throw;
                }
                catch (IOException)
                {
                    MarkBroken();
                    throw;
                }
            }
            finally
            {
                _commandGate.Release();
                TryDisposeSocketIfRetired();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _retired, 1) != 0) return;

            // Abort interrupts active I/O without racing ClientWebSocket.Dispose against a command.
            // The actual dispose is attempted only after exclusively reacquiring the command gate.
            try { _socket.Abort(); } catch { }
            TryDisposeSocketIfRetired();
        }

        private void TryDisposeSocketIfRetired()
        {
            if (Volatile.Read(ref _retired) == 0 || Volatile.Read(ref _socketDisposed) != 0) return;
            if (!_commandGate.Wait(0)) return;
            try
            {
                if (Volatile.Read(ref _retired) != 0)
                    DisposeSocketUnderGate();
            }
            finally
            {
                _commandGate.Release();
            }
        }

        private void DisposeSocketUnderGate()
        {
            if (Interlocked.Exchange(ref _socketDisposed, 1) != 0) return;
            try { _socket.Dispose(); } catch { }
        }

        private void MarkBroken()
        {
            if (Interlocked.Exchange(ref _broken, 1) != 0) return;
            try { _socket.Abort(); } catch { }
        }
    }
}
