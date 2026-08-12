using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace GPTDeskTop.Services;

internal static class ChromeTransportFailureClassifier
{
    private static readonly string[] TargetLifecycleMarkers =
    [
        "Inspected target navigated or closed",
        "No target with given id",
        "Cannot find target",
        "Target closed",
        "Session closed",
        "Execution context was destroyed",
        "Context was destroyed",
        "Cannot find context with specified id",
        "Cannot find default execution context",
        "Cannot find execution context",
        "Frame with given id not found",
        "Cannot find frame with id",
        "Navigating frame was detached",
        "Frame was detached",
        "Execution context is not available in detached frame",
        "Target crashed",
        "Renderer process gone",
        "page, context or browser has been closed"
    ];

    private static readonly string[] TransientTransportMarkers =
    [
        "Chrome closed the DevTools connection",
        "Chrome DevTools session was invalidated",
        "session was invalidated",
        "connection was forcibly closed",
        "forcibly closed by the remote host",
        "remote party closed the WebSocket connection without completing the close handshake",
        "unable to connect",
        "connection refused",
        "actively refused",
        "connection reset",
        "broken pipe",
        "WebSocket is not connected",
        "transport connection",
        "Promise was collected"
    ];

    private static readonly string[] ExpectedBrowserCloseMarkers =
    [
        "Chrome closed the DevTools connection",
        "connection was forcibly closed",
        "forcibly closed by the remote host",
        "remote party closed the WebSocket connection without completing the close handshake",
        "connection reset",
        "session was invalidated",
        "WebSocket is not connected"
    ];

    public static bool IsTargetLifecycleError(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : error.ToString();
        return IsTargetLifecycleMessage(message);
    }

    public static bool IsTargetLifecycleMessage(string? message)
        => ContainsAny(message, TargetLifecycleMarkers);

    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var current in EnumerateExceptionTree(exception))
        {
            if (current is WebSocketException
                or SocketException
                or IOException
                or TimeoutException
                or HttpRequestException
                or ObjectDisposedException
                or TaskCanceledException)
                return true;

            if (IsTargetLifecycleMessage(current.Message)
                || ContainsAny(current.Message, TransientTransportMarkers))
                return true;
        }

        return false;
    }

    public static bool IsExpectedBrowserCloseDisconnect(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var current in EnumerateExceptionTree(exception))
        {
            if (current is WebSocketException or SocketException or ObjectDisposedException)
                return true;
            if (ContainsAny(current.Message, ExpectedBrowserCloseMarkers))
                return true;
        }

        return false;
    }

    private static IEnumerable<Exception> EnumerateExceptionTree(Exception root)
    {
        var pending = new Stack<Exception>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }

    private static bool ContainsAny(string? message, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        for (var index = 0; index < markers.Count; index++)
        {
            if (message.Contains(markers[index], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
