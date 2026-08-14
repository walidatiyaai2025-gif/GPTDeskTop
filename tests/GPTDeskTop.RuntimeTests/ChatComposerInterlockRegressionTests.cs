namespace GPTDeskTop.RuntimeTests;

public sealed class ChatComposerInterlockRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void GeneratingStateNeverAllowsEditorPreparationOrSubmit()
    {
        var prepare = Services.ChatComposerInterlockPolicy.DecideBeforeEditorMutation(
            isGenerating: true,
            editorPresent: true,
            editorEnabled: true,
            hasRenderedError: false);
        var submit = Services.ChatComposerInterlockPolicy.DecideBeforeSubmit(
            isGenerating: true,
            editorPresent: true,
            editorEnabled: true,
            sendButtonPresent: true,
            sendButtonEnabled: true,
            hasRenderedError: false);

        Assert.Equal(Services.ComposerAutomationDecision.DeferWhileGenerating, prepare);
        Assert.Equal(Services.ComposerAutomationDecision.DeferWhileGenerating, submit);
    }

    [Fact]
    public void SendPathReadsInterlockBeforeAnyEditorMutationAndNeverSynthesizesEnter()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        var start = source.IndexOf("public async Task<bool> SendChatMessageAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var sendMethod = source[start..end];

        var gateIndex = sendMethod.IndexOf("ReadComposerDecisionAsync(tab, requireSendReady: false", StringComparison.Ordinal);
        var focusIndex = sendMethod.IndexOf("editor.focus()", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0 && focusIndex > gateIndex, "Composer readiness must be observed before editor.focus/input mutation.");
        Assert.Contains("DecideBeforeSubmit", source, StringComparison.Ordinal);
        Assert.Contains("if (visible(stop)) return false;", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.dispatchKeyEvent", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('[contenteditable=\"true\"]')", sendMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledOrGeneratingComposerIsAPassiveDeferState()
    {
        Assert.Equal(
            Services.ComposerAutomationDecision.DeferUntilEditorReady,
            Services.ChatComposerInterlockPolicy.DecideBeforeEditorMutation(false, true, false, false));
        Assert.Equal(
            Services.ComposerAutomationDecision.DeferUntilSendReady,
            Services.ChatComposerInterlockPolicy.DecideBeforeSubmit(false, true, true, true, false, false));
    }
}
