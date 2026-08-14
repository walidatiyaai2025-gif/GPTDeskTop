namespace GPTDeskTop.RuntimeTests;

public sealed class ComposerVoiceButtonSendFallbackRegressionTests
{
    private static string ReadChromeServiceSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        return File.ReadAllText(path);
    }

    private static string ReadSendMethod()
    {
        var source = ReadChromeServiceSource();
        var start = source.IndexOf("public async Task<bool> SendChatMessageAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    [Fact]
    public void ComposerSubmitUsesCanonicalSendButtonWithoutKeyboardFallback()
    {
        var source = ReadChromeServiceSource();
        var sendMethod = ReadSendMethod();

        Assert.Contains("button[data-testid=\"send-button\"]", source, StringComparison.Ordinal);
        Assert.Contains("DecideBeforeSubmit", source, StringComparison.Ordinal);
        Assert.Contains("sendButton.click();", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("fallbackReady", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.dispatchKeyEvent", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsVirtualKeyCode = 13", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("nativeVirtualKeyCode = 13", sendMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveStopOrDisabledComposerDefersInsteadOfSynthesizingEnter()
    {
        var sendMethod = ReadSendMethod();

        Assert.Contains("ReadComposerDecisionAsync(tab, requireSendReady: false", sendMethod, StringComparison.Ordinal);
        Assert.Contains("if (visible(stop)) return false;", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("const editorText =", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("rawKeyDown", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("keyUp", sendMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void RealSendButtonRemainsTheOnlySubmitMutationAfterReadinessGate()
    {
        var sendMethod = ReadSendMethod();
        var gate = sendMethod.IndexOf("ReadComposerDecisionAsync(tab, requireSendReady: false", StringComparison.Ordinal);
        var click = sendMethod.IndexOf("sendButton.click();", StringComparison.Ordinal);

        Assert.True(gate >= 0);
        Assert.True(click > gate);
        Assert.DoesNotContain("Input.dispatchKeyEvent", sendMethod, StringComparison.Ordinal);
    }
}
