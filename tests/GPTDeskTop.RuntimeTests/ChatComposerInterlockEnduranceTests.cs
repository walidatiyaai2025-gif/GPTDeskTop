using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChatComposerInterlockEnduranceTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void DisabledSendRecoversToExactlyOneReadyDecisionAfterResponseCompletes()
    {
        var decisions = new[]
        {
            ChatComposerInterlockPolicy.DecideBeforeSubmit(
                isGenerating: true, editorPresent: true, editorEnabled: true,
                sendButtonPresent: false, sendButtonEnabled: false, hasRenderedError: false),
            ChatComposerInterlockPolicy.DecideBeforeSubmit(
                isGenerating: false, editorPresent: true, editorEnabled: true,
                sendButtonPresent: true, sendButtonEnabled: false, hasRenderedError: false),
            ChatComposerInterlockPolicy.DecideBeforeSubmit(
                isGenerating: false, editorPresent: true, editorEnabled: true,
                sendButtonPresent: true, sendButtonEnabled: true, hasRenderedError: false)
        };

        Assert.Equal(ComposerAutomationDecision.DeferWhileGenerating, decisions[0]);
        Assert.Equal(ComposerAutomationDecision.DeferUntilSendReady, decisions[1]);
        Assert.Equal(ComposerAutomationDecision.ReadyToSend, decisions[2]);
        Assert.Single(decisions.Where(x => x == ComposerAutomationDecision.ReadyToSend));
    }

    [Fact]
    public void LongGeneratingWindowNeverBecomesMutationReadyRegardlessOfPollCount()
    {
        for (var poll = 0; poll < 10_000; poll++)
        {
            var prepare = ChatComposerInterlockPolicy.DecideBeforeEditorMutation(
                isGenerating: true,
                editorPresent: true,
                editorEnabled: true,
                hasRenderedError: false);

            Assert.Equal(ComposerAutomationDecision.DeferWhileGenerating, prepare);
        }
    }

    [Fact]
    public void RuntimeDiagnosticsExposeOnlyDecisionReasonAndTimestamp()
    {
        _ = ChatComposerInterlockPolicy.DecideBeforeSubmit(
            isGenerating: false,
            editorPresent: true,
            editorEnabled: true,
            sendButtonPresent: true,
            sendButtonEnabled: false,
            hasRenderedError: false);

        var snapshot = ChatComposerDecisionDiagnostics.Last;
        Assert.Equal(ComposerAutomationDecision.DeferUntilSendReady, snapshot.Decision);
        Assert.Equal("send-not-ready", snapshot.Reason);
        Assert.True(snapshot.ObservedAtUtc > DateTimeOffset.MinValue);

        var properties = typeof(ComposerDecisionSnapshot).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(properties, x => x.Contains("Prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, x => x.Contains("Message", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, x => x.Contains("Text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomationTargetsOnlyCanonicalPromptEditorAndNeverGenericContentEditable()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        var readiness = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatComposerReadinessScript.cs"));

        Assert.Contains("#prompt-textarea", source, StringComparison.Ordinal);
        Assert.Contains("textarea[placeholder]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('[contenteditable=\"true\"]')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("querySelectorAll('textarea,[contenteditable=\"true\"]')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[contenteditable=\"true\"]", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.dispatchKeyEvent", source, StringComparison.Ordinal);
    }
}
