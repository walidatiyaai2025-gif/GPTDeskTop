using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class NewChatMonitorWorkflowRegressionTests
{
    [Fact]
    public async Task LastUsedWorkflowMessagesPersistAcrossDatabaseReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "flow.db");

        try
        {
            var first = new LocalDatabase(dbPath);
            await first.InitializeAsync();
            await first.SetSettingAsync("NewChatBootstrapMessage", "bootstrap-A");
            await first.SetSettingAsync("NewChatMonitorAutoReply", "monitor-B");

            var reopened = new LocalDatabase(dbPath);
            await reopened.InitializeAsync();
            Assert.Equal("bootstrap-A", await reopened.GetSettingAsync("NewChatBootstrapMessage"));
            Assert.Equal("monitor-B", await reopened.GetSettingAsync("NewChatMonitorAutoReply"));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void WorkflowUsesSingleVerifiedSendThenReadOnlyStableReconciliationBeforeRegistrationAndStart()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));
        var selector = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatStableTargetSelector.cs"));

        var createIndex = source.IndexOf("CreateFreshChatTabAsync", StringComparison.Ordinal);
        var verifiedSendIndex = source.IndexOf("SendInitialMessageVerifiedAsync(openedTab", StringComparison.Ordinal);
        var stableIdentityIndex = source.IndexOf("ResolveStableConversationAsync(", verifiedSendIndex, StringComparison.Ordinal);
        var reconcileIndex = source.IndexOf("ReconcileInitialMessageOnStableConversationAsync(", stableIdentityIndex, StringComparison.Ordinal);
        var registerIndex = source.IndexOf("RegisterMonitorIfConversationAvailableAsync", StringComparison.Ordinal);
        var startIndex = source.IndexOf("StartMonitorAsync(savedMonitor, stableTab)", StringComparison.Ordinal);
        var resumeIntentIndex = source.IndexOf("SetMonitorDesiredRunningAsync", StringComparison.Ordinal);

        Assert.True(createIndex >= 0);
        Assert.True(verifiedSendIndex > createIndex);
        Assert.True(stableIdentityIndex > verifiedSendIndex);
        Assert.True(reconcileIndex > stableIdentityIndex);
        Assert.True(registerIndex > reconcileIndex);
        Assert.True(startIndex > registerIndex);
        Assert.True(resumeIntentIndex > startIndex);

        Assert.Contains("PreexistingTargetIds", source, StringComparison.Ordinal);
        Assert.Contains("NewChatStableTargetSelector.Select", source, StringComparison.Ordinal);
        Assert.Contains("NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap", source, StringComparison.Ordinal);
        Assert.Contains("GetChatStateAsync(stableTab", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(source, "SendChatMessageVerifiedAsync("));
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl", selector, StringComparison.Ordinal);
        Assert.Contains("!preexistingTargetIds.Contains(tab.Id)", selector, StringComparison.Ordinal);
        Assert.Contains("replacements.Count == 1", selector, StringComparison.Ordinal);
        Assert.Contains("NewChatBootstrapSent", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    public void FreshStableResponseActivityConfirmsAcceptedBootstrap(
        int assistantCount,
        bool isGenerating,
        bool hasRenderedError)
    {
        Assert.True(NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap(
            isStableConversation: true,
            targetExistedBeforeWorkflow: false,
            assistantCount,
            isGenerating,
            hasRenderedError));
    }

    [Fact]
    public void ReconciliationFailsClosedWithoutFreshStableResponseEvidence()
    {
        Assert.False(NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap(
            isStableConversation: true,
            targetExistedBeforeWorkflow: false,
            assistantCount: 0,
            isGenerating: false,
            hasRenderedError: false));

        Assert.False(NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap(
            isStableConversation: false,
            targetExistedBeforeWorkflow: false,
            assistantCount: 1,
            isGenerating: true,
            hasRenderedError: false));

        Assert.False(NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap(
            isStableConversation: true,
            targetExistedBeforeWorkflow: true,
            assistantCount: 1,
            isGenerating: true,
            hasRenderedError: false));
    }

    [Fact]
    public void BootstrapRecoveryNeverStartsAnotherPhysicalSendAfterUncertainOutcome()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        var sendMethodStart = source.IndexOf("private async Task<bool> SendInitialMessageVerifiedAsync", StringComparison.Ordinal);
        var reconcileMethodStart = source.IndexOf("private async Task<bool> ReconcileInitialMessageOnStableConversationAsync", sendMethodStart, StringComparison.Ordinal);
        var resolveMethodStart = source.IndexOf("private async Task<ChromeTab?> ResolveStableConversationAsync", reconcileMethodStart, StringComparison.Ordinal);
        Assert.True(sendMethodStart >= 0 && reconcileMethodStart > sendMethodStart && resolveMethodStart > reconcileMethodStart);

        var sendMethod = source[sendMethodStart..reconcileMethodStart];
        var reconcileMethod = source[reconcileMethodStart..resolveMethodStart];

        Assert.Equal(1, CountOccurrences(sendMethod, "SendChatMessageVerifiedAsync("));
        Assert.DoesNotContain("ReloadTabAsync", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("for (var attempt = 1; attempt <= 3", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessage", reconcileMethod, StringComparison.Ordinal);
        Assert.Contains("GetChatStateAsync", reconcileMethod, StringComparison.Ordinal);
        Assert.Contains("Do not retry here", sendMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientChromeFailuresAreRecoveryStateInsteadOfExceptionLogFlood()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        Assert.Contains("ChromeTransportFailureClassifier.IsTransient(ex)", source, StringComparison.Ordinal);
        Assert.Contains("The physical outcome may be uncertain during target navigation. Do not retry here.", source, StringComparison.Ordinal);
        Assert.Contains("The stable target can still be rebinding while the first response is materializing.", source, StringComparison.Ordinal);
        Assert.Contains("Navigation/CDP churn while the new chat receives its /c/{id} identity is recoverable.", source, StringComparison.Ordinal);

        var sendMethodStart = source.IndexOf("private async Task<bool> SendInitialMessageVerifiedAsync", StringComparison.Ordinal);
        var reconcileMethodStart = source.IndexOf("private async Task<bool> ReconcileInitialMessageOnStableConversationAsync", sendMethodStart, StringComparison.Ordinal);
        Assert.True(sendMethodStart >= 0 && reconcileMethodStart > sendMethodStart);
        var sendMethod = source[sendMethodStart..reconcileMethodStart];

        var transientCatch = sendMethod.IndexOf("ChromeTransportFailureClassifier.IsTransient(ex)", StringComparison.Ordinal);
        var persistentLog = sendMethod.IndexOf("ExceptionLogService.Log(ex, \"NewChatMonitorWorkflow.InitialVerifiedSend\")", StringComparison.Ordinal);
        Assert.True(transientCatch >= 0 && persistentLog > transientCatch);
    }

    [Fact]
    public void PersistentVerifiedSendFailureUsesConnectionAccurateOperatorMessage()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        Assert.Contains(
            "The initial ChatGPT message could not be verified after stable-conversation recovery.",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ChatGPT did not produce a verified user-message receipt for the initial chat message.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowExposesOneActionWithTwoIndependentMessages()
    {
        var mainForm = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "MainForm.cs"));
        var dialog = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "NewChatMonitorForm.cs"));

        Assert.Contains("New Chat + Monitor", mainForm, StringComparison.Ordinal);
        Assert.Contains("CreateNewChatMonitorAsync", mainForm, StringComparison.Ordinal);
        Assert.Contains("NewChatBootstrapMessage", mainForm, StringComparison.Ordinal);
        Assert.Contains("NewChatMonitorAutoReply", mainForm, StringComparison.Ordinal);
        Assert.Contains("NewChatMonitorWorkflowService", mainForm, StringComparison.Ordinal);
        Assert.Contains("Initial Chat Message", dialog, StringComparison.Ordinal);
        Assert.Contains("Monitor Auto Reply", dialog, StringComparison.Ordinal);
        Assert.Contains("Create Chat + Start Monitor", dialog, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
