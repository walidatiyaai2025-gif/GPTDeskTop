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

    [Fact]
    public void ComposerSubmitDoesNotDependExclusivelyOnVisibleSendButton()
    {
        var source = ReadChromeServiceSource();

        Assert.Contains("button[data-testid=\"send-button\"]", source, StringComparison.Ordinal);
        Assert.Contains("fallbackReady", source, StringComparison.Ordinal);
        Assert.Contains("Input.dispatchKeyEvent", source, StringComparison.Ordinal);
        Assert.Contains("type = \"rawKeyDown\"", source, StringComparison.Ordinal);
        Assert.Contains("type = \"keyUp\"", source, StringComparison.Ordinal);
        Assert.Contains("windowsVirtualKeyCode = 13", source, StringComparison.Ordinal);
        Assert.Contains("nativeVirtualKeyCode = 13", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnterFallbackRequiresTextAndNoActiveStopControl()
    {
        var source = ReadChromeServiceSource();

        Assert.Contains("const editorText =", source, StringComparison.Ordinal);
        Assert.Contains("if (!editorText.trim()) return { clicked: false, fallbackReady: false };", source, StringComparison.Ordinal);
        Assert.Contains("button[data-testid=\"stop-button\"]", source, StringComparison.Ordinal);
        Assert.Contains("return { clicked: false, fallbackReady: !visible(stopButton) };", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RealSendButtonRemainsPrimaryBeforeKeyboardFallback()
    {
        var source = ReadChromeServiceSource();
        var sendButtonClick = source.IndexOf("sendButton.click();", StringComparison.Ordinal);
        var keyFallback = source.IndexOf("Input.dispatchKeyEvent", StringComparison.Ordinal);

        Assert.True(sendButtonClick >= 0);
        Assert.True(keyFallback > sendButtonClick);
    }
}
