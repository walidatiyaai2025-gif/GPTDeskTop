using GPTDeskTop.Data;

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
    public void WorkflowRequiresVerifiedSendThenStableIdentityBeforeMonitorRegistrationAndStart()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        var createIndex = source.IndexOf("CreateFreshChatTabAsync", StringComparison.Ordinal);
        var verifiedSendIndex = source.IndexOf("SendInitialMessageVerifiedAsync(openedTab", StringComparison.Ordinal);
        var stableIdentityIndex = source.IndexOf("ResolveStableConversationAsync(openedTab", StringComparison.Ordinal);
        var registerIndex = source.IndexOf("RegisterMonitorIfConversationAvailableAsync", StringComparison.Ordinal);
        var startIndex = source.IndexOf("StartMonitorAsync(savedMonitor, stableTab)", StringComparison.Ordinal);
        var resumeIntentIndex = source.IndexOf("SetMonitorDesiredRunningAsync", StringComparison.Ordinal);

        Assert.True(createIndex >= 0);
        Assert.True(verifiedSendIndex > createIndex);
        Assert.True(stableIdentityIndex > verifiedSendIndex);
        Assert.True(registerIndex > stableIdentityIndex);
        Assert.True(startIndex > registerIndex);
        Assert.True(resumeIntentIndex > startIndex);
        Assert.Contains("SendChatMessageVerifiedAsync", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl", source, StringComparison.Ordinal);
        Assert.Contains("NewChatBootstrapSent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientChromeFailuresAreRecoveryStateInsteadOfExceptionLogFlood()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        Assert.Contains("ChromeTransportFailureClassifier.IsTransient(ex)", source, StringComparison.Ordinal);
        Assert.Contains("Verified send retires broken sessions and re-checks the DOM before a resend.", source, StringComparison.Ordinal);
        Assert.Contains("Reload may race a navigation or a retired target session.", source, StringComparison.Ordinal);
        Assert.Contains("Navigation/CDP churn while the new chat receives its /c/{id} identity is recoverable.", source, StringComparison.Ordinal);

        var sendMethodStart = source.IndexOf("private async Task<bool> SendInitialMessageVerifiedAsync", StringComparison.Ordinal);
        var resolveMethodStart = source.IndexOf("private async Task<ChromeTab?> ResolveStableConversationAsync", sendMethodStart, StringComparison.Ordinal);
        Assert.True(sendMethodStart >= 0 && resolveMethodStart > sendMethodStart);
        var sendMethod = source[sendMethodStart..resolveMethodStart];

        var transientCatch = sendMethod.IndexOf("ChromeTransportFailureClassifier.IsTransient(ex)", StringComparison.Ordinal);
        var persistentLog = sendMethod.IndexOf("ExceptionLogService.Log(ex, $\"NewChatMonitorWorkflow.InitialSendAttempt", StringComparison.Ordinal);
        Assert.True(transientCatch >= 0 && persistentLog > transientCatch);
    }

    [Fact]
    public void PersistentVerifiedSendFailureUsesConnectionAccurateOperatorMessage()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs"));

        Assert.Contains(
            "The initial ChatGPT message could not be verified after automatic Chrome/CDP recovery.",
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

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}
