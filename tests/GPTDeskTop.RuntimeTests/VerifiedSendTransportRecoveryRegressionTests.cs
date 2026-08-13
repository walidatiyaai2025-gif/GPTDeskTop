namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendTransportRecoveryRegressionTests
{
    private static string ServiceSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void InitialVerifiedSendSnapshotUsesBoundedTransientRecoveryInsteadOfEscapingTimeout()
    {
        var source = ServiceSource();
        var methodStart = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(helperStart > methodStart);

        var method = source[methodStart..helperStart];
        Assert.Contains("var deadline = DateTimeOffset.UtcNow.AddSeconds(30);", method, StringComparison.Ordinal);
        Assert.Contains("var before = await TryGetUserMessageSnapshotAsync", method, StringComparison.Ordinal);
        Assert.Contains("while (!before.Success && DateTimeOffset.UtcNow < deadline)", method, StringComparison.Ordinal);
        Assert.Contains("if (!before.Success) return false;", method, StringComparison.Ordinal);
        Assert.DoesNotContain("var before = await GetUserMessageSnapshotAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverableRuntimeEvaluateTimeoutRetiresSessionWithoutWritingCrashNoise()
    {
        var source = ServiceSource();
        var helperStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", StringComparison.Ordinal);
        var rawSnapshotStart = source.IndexOf("private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync", helperStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(rawSnapshotStart > helperStart);

        var helper = source[helperStart..rawSnapshotStart];
        Assert.Contains("IsRecoverableMonitorTransportException(ex)", helper, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Invalidate(tab.Id);", helper, StringComparison.Ordinal);
        Assert.Contains("return (false, 0, string.Empty);", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void SendTimeoutVerifiesDomBeforeAnyFurtherSendAttempt()
    {
        var source = ServiceSource();
        var methodStart = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..helperStart];

        var currentSnapshot = method.IndexOf("var current = await TryGetUserMessageSnapshotAsync", StringComparison.Ordinal);
        var send = method.IndexOf("SendChatMessageAsync(tab, message", StringComparison.Ordinal);
        var transientCatch = method.IndexOf("IsRecoverableMonitorTransportException(ex)", send, StringComparison.Ordinal);
        var invalidate = method.IndexOf("_sessionPool.Invalidate(tab.Id);", transientCatch, StringComparison.Ordinal);
        var continueAfterTimeout = method.IndexOf("continue;", invalidate, StringComparison.Ordinal);

        Assert.True(currentSnapshot >= 0 && send > currentSnapshot);
        Assert.True(transientCatch > send && invalidate > transientCatch && continueAfterTimeout > invalidate);
        Assert.DoesNotContain("ChromeDevToolsService.VerifySend", method, StringComparison.Ordinal);
    }
}
