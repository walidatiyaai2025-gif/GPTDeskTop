using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeTransportFailureClassifierMatrixTests
{
    private static readonly Type ClassifierType = typeof(ChromeDevToolsService).Assembly.GetType(
        "GPTDeskTop.Services.ChromeTransportFailureClassifier")
        ?? throw new InvalidOperationException("Chrome transport classifier type was not found.");

    private static readonly MethodInfo TargetLifecycleMethod = GetMethod("IsTargetLifecycleMessage");
    private static readonly MethodInfo TransientMethod = GetMethod("IsTransient");
    private static readonly MethodInfo ExpectedCloseMethod = GetMethod("IsExpectedBrowserCloseDisconnect");

    // MON-STAB-050 acceptance matrix cases 01-18.
    [Theory]
    [InlineData("Inspected target navigated or closed")]
    [InlineData("No target with given id found")]
    [InlineData("Cannot find target")]
    [InlineData("Target closed")]
    [InlineData("Session closed")]
    [InlineData("Execution context was destroyed.")]
    [InlineData("Context was destroyed")]
    [InlineData("Cannot find context with specified id")]
    [InlineData("Cannot find default execution context")]
    [InlineData("Cannot find execution context")]
    [InlineData("Frame with given id not found")]
    [InlineData("Cannot find frame with id")]
    [InlineData("Navigating frame was detached")]
    [InlineData("Frame was detached")]
    [InlineData("Execution context is not available in detached frame")]
    [InlineData("Target crashed")]
    [InlineData("Renderer process gone")]
    [InlineData("Target page, context or browser has been closed")]
    public void TargetLifecycleMessagesAreRecoverable(string message)
        => Assert.True(InvokeBool(TargetLifecycleMethod, message));

    // MON-STAB-050 acceptance matrix cases 19-31.
    [Theory]
    [InlineData("Chrome closed the DevTools connection")]
    [InlineData("Chrome DevTools session was invalidated")]
    [InlineData("session was invalidated")]
    [InlineData("connection was forcibly closed")]
    [InlineData("forcibly closed by the remote host")]
    [InlineData("The remote party closed the WebSocket connection without completing the close handshake")]
    [InlineData("unable to connect to remote server")]
    [InlineData("connection refused")]
    [InlineData("No connection could be made because the target machine actively refused it")]
    [InlineData("connection reset by peer")]
    [InlineData("broken pipe")]
    [InlineData("WebSocket is not connected")]
    [InlineData("Promise was collected")]
    public void TransientTransportMessagesAreRecoverable(string message)
        => Assert.True(InvokeBool(TransientMethod, new InvalidOperationException(message)));

    [Fact]
    public void Http500WebSocketUpgradeFailureIsTransientEvenWhenWrappedAsGenericException()
        => Assert.True(InvokeBool(TransientMethod,
            new InvalidOperationException("The server returned status code '500' when status code '101' was expected.")));

    [Fact]
    public void AnyFailedWebSocketUpgradeThatExpected101IsTransient()
        => Assert.True(InvokeBool(TransientMethod,
            new InvalidOperationException("The server returned status code '403' when status code '101' was expected.")));

    // MON-STAB-050 acceptance matrix cases 32-38.
    [Fact]
    public void WebSocketExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new WebSocketException()));

    [Fact]
    public void IOExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new IOException("CDP transport failed")));

    [Fact]
    public void TimeoutExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new TimeoutException("CDP timed out")));

    [Fact]
    public void HttpRequestExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new HttpRequestException("debug endpoint unavailable")));

    [Fact]
    public void ObjectDisposedExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new ObjectDisposedException("NetworkStream")));

    [Fact]
    public void TaskCanceledExceptionIsTransientAtBoundary()
        => Assert.True(InvokeBool(TransientMethod, new TaskCanceledException("transport operation aborted")));

    [Fact]
    public void SocketExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod, new SocketException(10054)));

    // MON-STAB-050 acceptance matrix cases 39-43.
    [Fact]
    public void BrowserCloseWebSocketDisconnectIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod, new WebSocketException()));

    [Fact]
    public void BrowserCloseDisposedSocketIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod, new ObjectDisposedException("ClientWebSocket")));

    [Fact]
    public void BrowserCloseSocketResetIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod, new SocketException(10054)));

    [Fact]
    public void BrowserCloseMissingHandshakeIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod,
            new InvalidOperationException("remote party closed the WebSocket connection without completing the close handshake")));

    [Fact]
    public void BrowserCloseResetMessageIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod,
            new IOException("connection reset by peer")));

    // MON-STAB-050 acceptance matrix cases 44-47.
    [Fact]
    public void NestedTransportExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod,
            new InvalidOperationException("outer", new IOException("inner transport failure"))));

    [Fact]
    public void AggregateTransportExceptionIsTransient()
        => Assert.True(InvokeBool(TransientMethod,
            new AggregateException(new InvalidOperationException("unrelated"), new HttpRequestException("endpoint unavailable"))));

    [Fact]
    public void NestedBrowserCloseDisposeIsExpected()
        => Assert.True(InvokeBool(ExpectedCloseMethod,
            new IOException("outer", new ObjectDisposedException("NetworkStream"))));

    [Fact]
    public void NestedTargetLifecycleMessageIsTransient()
        => Assert.True(InvokeBool(TransientMethod,
            new InvalidOperationException("outer", new InvalidOperationException("Cannot find default execution context"))));

    // MON-STAB-050 acceptance matrix cases 48-50.
    [Fact]
    public void InvalidParametersRemainPersistent()
    {
        var exception = new InvalidOperationException("Chrome DevTools error: Invalid parameters");
        Assert.False(InvokeBool(TargetLifecycleMethod, exception.Message));
        Assert.False(InvokeBool(TransientMethod, exception));
    }

    [Fact]
    public void ArbitraryJavaScriptFailureRemainsPersistent()
        => Assert.False(InvokeBool(TransientMethod,
            new InvalidOperationException("TypeError: Cannot read properties of undefined")));

    [Fact]
    public void ChatGptUiErrorTextIsNotAChromeTransportFailure()
        => Assert.False(InvokeBool(TransientMethod,
            new InvalidOperationException("Something went wrong while generating the response")));

    [Fact]
    public void SessionPoolDelegatesLifecycleMatchingToSharedClassifier()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains(
            "=> ChromeTransportFailureClassifier.IsTargetLifecycleError(error);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "message.Contains(\"Cannot find default execution context\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifierContainsTheProductionDefaultContextMarkerExactlyOnce()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeTransportFailureClassifier.cs"));

        Assert.Equal(
            1,
            source.Split("Cannot find default execution context", StringSplitOptions.None).Length - 1);
    }

    private static MethodInfo GetMethod(string name)
        => ClassifierType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"Classifier method '{name}' was not found.");

    private static bool InvokeBool(MethodInfo method, object argument)
        => (bool)(method.Invoke(null, [argument])
                  ?? throw new InvalidOperationException($"Classifier method '{method.Name}' returned null."));

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
