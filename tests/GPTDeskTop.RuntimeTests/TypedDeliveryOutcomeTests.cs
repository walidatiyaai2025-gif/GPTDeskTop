using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class TypedDeliveryOutcomeTests
{
    [Fact]
    public async Task PreparationFailureIsDefinitelyNotSubmitted()
    {
        await using var endpoint = new FakeCdp { FailPreparation = true };
        var result = await endpoint.Chrome.SendChatMessageWithOutcomeAsync(endpoint.Tab, "test", requireNewTurn: true);
        Assert.Equal(VerifiedDeliveryOutcome.NotSubmitted, result);
        Assert.Equal(0, endpoint.Clicks);
    }

    [Fact]
    public async Task ContentEditablePreparationUsesCdpNativeInputBeforeSingleSendClick()
    {
        await using var endpoint = new FakeCdp { GenerateAfterClick = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var outcome = await endpoint.Chrome.SendChatMessageWithOutcomeAsync(
            endpoint.Tab, "test", stop.Token, requireNewTurn: true);

        Assert.Equal(VerifiedDeliveryOutcome.Delivered, outcome);
        Assert.Equal(1, endpoint.InputInsertions);
        Assert.Equal(1, endpoint.Clicks);
    }

    [Fact]
    public async Task ComposerTextMismatchNeverAuthorizesPhysicalSubmit()
    {
        await using var endpoint = new FakeCdp { DropInsertedText = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var submitted = await endpoint.Chrome.SendChatMessageAsync(endpoint.Tab, "test", stop.Token);

        Assert.False(submitted);
        Assert.Equal(1, endpoint.InputInsertions);
        Assert.Equal(0, endpoint.Clicks);
    }

    [Fact]
    public async Task ExplicitNoClickReplyRemainsDefinitelyUnsent()
    {
        await using var endpoint = new FakeCdp { RejectClick = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var outcome = await endpoint.Chrome.SendChatMessageWithOutcomeAsync(endpoint.Tab, "test", stop.Token, requireNewTurn: true);
        Assert.Equal(VerifiedDeliveryOutcome.NotSubmitted, outcome);
        Assert.Equal(0, endpoint.Clicks);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ClickOrLostClickReplyReconcilesReadOnlyUntilMatchingReceipt(bool loseReply, bool promote)
    {
        await using var endpoint = new FakeCdp { LoseClickReply = loseReply, PromoteTarget = promote };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var send = endpoint.Chrome.SendChatMessageWithOutcomeAsync(endpoint.Tab, "test", stop.Token, requireNewTurn: true);
        await endpoint.Clicked.Task.WaitAsync(stop.Token);
        // The target has promoted to /c/id, but that alone must not checkpoint delivery.
        await Task.Delay(1800, stop.Token);
        Assert.False(send.IsCompleted);
        Assert.Equal(1, endpoint.Clicks);
        Assert.Equal(0, endpoint.Reloads);
        endpoint.ShowReceipt = true;
        Assert.Equal(VerifiedDeliveryOutcome.Delivered, await send.WaitAsync(stop.Token));
        Assert.Equal(1, endpoint.Clicks);
        Assert.Equal(0, endpoint.Reloads);
    }

    [Fact]
    public async Task SameTargetGenerationAfterDispatchConfirmsDeliveryWithoutSecondClick()
    {
        await using var endpoint = new FakeCdp { GenerateAfterClick = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var outcome = await endpoint.Chrome.SendChatMessageWithOutcomeAsync(
            endpoint.Tab, "test", stop.Token, requireNewTurn: true);
        Assert.Equal(VerifiedDeliveryOutcome.Delivered, outcome);
        Assert.Equal(1, endpoint.Clicks);
        Assert.Equal(0, endpoint.Reloads);
    }

    [Fact]
    public async Task NoReceiptOrGenerationTerminatesAmbiguousWithoutSecondClick()
    {
        await using var endpoint = new FakeCdp();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var outcome = await endpoint.Chrome.SendChatMessageWithOutcomeAsync(
            endpoint.Tab,
            "test",
            stop.Token,
            requireNewTurn: true,
            readOnlyReconciliation: true,
            readOnlyReconciliationTimeout: TimeSpan.FromSeconds(2));
        Assert.Equal(VerifiedDeliveryOutcome.Ambiguous, outcome);
        Assert.Equal(1, endpoint.Clicks);
        Assert.Equal(0, endpoint.Reloads);
    }

    [Fact]
    public async Task CancellationAfterDispatchCannotReturnUnsentOrClickAgain()
    {
        await using var endpoint = new FakeCdp { LoseClickReply = true };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var send = endpoint.Chrome.SendChatMessageWithOutcomeAsync(endpoint.Tab, "test", stop.Token, requireNewTurn: true);
        await endpoint.Clicked.Task.WaitAsync(stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(1, endpoint.Clicks);
        Assert.Equal(0, endpoint.Reloads);
    }

    [Theory]
    [InlineData("target", "https://chatgpt.com/", "target", "https://chatgpt.com/c/new", true)]
    [InlineData("target", "https://chatgpt.com/", "other", "https://chatgpt.com/c/new", false)]
    [InlineData("target", "https://chatgpt.com/c/old", "target", "https://chatgpt.com/c/new", false)]
    [InlineData("target", "https://chatgpt.com/c/old", "replacement", "https://chatgpt.com/c/old", true)]
    public void RebindCannotAdoptAnUnrelatedConversation(string id, string origin, string liveId, string liveUrl, bool allowed)
        => Assert.Equal(allowed, MonitorDeliveryRecoveryPolicy.IsDeliveryBindingAllowed(id, origin, liveId, liveUrl));

    [Fact]
    public void OnlyExplicitNoClickAcknowledgementClearsAmbiguity()
    {
        var attempt = new VerifiedDeliveryAttempt();
        Assert.Equal(VerifiedDeliveryOutcome.NotSubmitted, attempt.Complete(false));
        attempt.RecordDispatch(true);
        Assert.Equal(VerifiedDeliveryOutcome.Ambiguous, attempt.Complete(false));
        attempt.RecordDispatch(false);
        Assert.Equal(VerifiedDeliveryOutcome.NotSubmitted, attempt.Complete(false));
        Assert.Equal(VerifiedDeliveryOutcome.Delivered, attempt.Complete(true));
    }

    private sealed class FakeCdp : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        private readonly HttpClient _http = new();
        private readonly List<Task> _clients = new();
        public bool FailPreparation;
        public bool LoseClickReply;
        public bool RejectClick;
        public bool PromoteTarget = true;
        public bool GenerateAfterClick;
        public bool DropInsertedText;
        public volatile bool ShowReceipt;
        public int InputInsertions;
        public int Clicks;
        public int Reloads;
        public string ComposerText = string.Empty;
        public TaskCompletionSource Clicked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ChromeDevToolsService Chrome { get; }
        public ChromeTab Tab { get; }
        private readonly string _socketUrl;

        public FakeCdp()
        {
            var portPicker = new TcpListener(IPAddress.Loopback, 0);
            portPicker.Start();
            var port = ((IPEndPoint)portPicker.LocalEndpoint).Port;
            portPicker.Stop();
            var root = $"http://127.0.0.1:{port}/";
            _socketUrl = $"ws://127.0.0.1:{port}/devtools/page/target";
            _listener.Prefixes.Add(root);
            _listener.Start();
            Tab = new ChromeTab { Id = "target", Url = "https://chatgpt.com/", Type = "page", WebSocketDebuggerUrl = _socketUrl };
            Chrome = new ChromeDevToolsService(_http, new ChromeConfig { DebuggingBaseUrl = root.TrimEnd('/') }, allowBrowserMutationRecovery: false);
            _server = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                    _clients.Add(HandleAsync(context));
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (!context.Request.IsWebSocketRequest)
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { new { id = "target", type = "page", title = "test", url = Clicks > 0 && PromoteTarget ? "https://chatgpt.com/c/new" : "https://chatgpt.com/", webSocketDebuggerUrl = _socketUrl } });
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes, _stop.Token);
                    context.Response.Close();
                    return;
                }
                using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var buffer = new byte[65536];
                while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult read;
                    do
                    {
                        read = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
                        if (read.MessageType == WebSocketMessageType.Close) return;
                        stream.Write(buffer, 0, read.Count);
                    } while (!read.EndOfMessage);
                    using var doc = JsonDocument.Parse(stream.ToArray());
                    var command = doc.RootElement;
                    var id = command.GetProperty("id").GetInt32();
                    var method = command.GetProperty("method").GetString();
                    if (method == "Page.reload") Interlocked.Increment(ref Reloads);

                    if (method == "Input.insertText")
                    {
                        Interlocked.Increment(ref InputInsertions);
                        var inserted = command.GetProperty("params").TryGetProperty("text", out var textElement)
                            ? textElement.GetString() ?? string.Empty
                            : string.Empty;
                        if (!DropInsertedText) ComposerText = inserted;
                        var insertedResponse = JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { } });
                        await socket.SendAsync(insertedResponse, WebSocketMessageType.Text, true, _stop.Token);
                        continue;
                    }

                    var expression = command.GetProperty("params").TryGetProperty("expression", out var e) ? e.GetString() ?? "" : "";
                    object value;
                    if (expression.Contains("sendButton.click()", StringComparison.Ordinal))
                    {
                        if (RejectClick || !string.Equals(ComposerText, "test", StringComparison.Ordinal))
                        {
                            FailPreparation = RejectClick;
                            var rejected = JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { result = new { type = "boolean", value = false } } });
                            await socket.SendAsync(rejected, WebSocketMessageType.Text, true, _stop.Token);
                            continue;
                        }
                        Interlocked.Increment(ref Clicks);
                        ComposerText = string.Empty;
                        Clicked.TrySetResult();
                        if (LoseClickReply) { socket.Abort(); return; }
                        value = true;
                    }
                    else if (expression.Contains("range.selectNodeContents(editor)", StringComparison.Ordinal))
                    {
                        if (FailPreparation)
                        {
                            var error = JsonSerializer.SerializeToUtf8Bytes(new { id, error = new { code = -32000, message = "Injected preparation failure" } });
                            await socket.SendAsync(error, WebSocketMessageType.Text, true, _stop.Token);
                            continue;
                        }
                        value = true;
                    }
                    else if (expression.Contains("return (text || '').trim() === expected", StringComparison.Ordinal))
                        value = string.Equals(ComposerText.Trim(), "test", StringComparison.Ordinal);
                    else if (expression.Contains("return { count:", StringComparison.Ordinal))
                        value = new { count = ShowReceipt ? 1 : 0, lastText = ShowReceipt ? "test" : "" };
                    else
                    {
                        var sendReady = string.Equals(ComposerText, "test", StringComparison.Ordinal);
                        value = new { isGenerating = GenerateAfterClick && Clicks > 0, editorPresent = true, editorEnabled = true, sendButtonPresent = sendReady, sendButtonEnabled = sendReady, assistantCount = 0, lastAssistantText = "", errorText = "" };
                    }
                    var response = JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { result = new { type = value is bool ? "boolean" : "object", value } } });
                    await socket.SendAsync(response, WebSocketMessageType.Text, true, _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (WebSocketException) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            await _server;
            await Task.WhenAll(_clients);
            _http.Dispose();
            _stop.Dispose();
        }
    }
}
