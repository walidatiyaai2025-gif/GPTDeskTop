namespace GPTDeskTop.RuntimeTests;

public sealed class BrandingNewChatWideMonitorRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SavedMonitorsAreDominantAndAutoReplyIsHiddenFromDashboard()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("ApplySplitterMinimumsWhenFeasible(_workspaceSplit, 240, 620)", source, StringComparison.Ordinal);
        Assert.Contains("SetSplitRatio(_workspaceSplit, 0.28)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderText = \"Auto reply\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentTheme.CreateMutedLabel(\"Auto reply\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstMessageVerificationRebindsAfterConversationNavigationWithoutDuplicateSend()
    {
        var workflow = ReadSource("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs");
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("if (!sent && stableTab is not null)", workflow, StringComparison.Ordinal);
        Assert.Contains("ReconcileInitialMessageOnStableConversationAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("NewChatBootstrapReconciliationPolicy.CanConfirmAcceptedBootstrap", workflow, StringComparison.Ordinal);
        Assert.Contains("GetChatStateAsync(stableTab", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("requireNewTurn: false", workflow, StringComparison.Ordinal);

        const string verifiedSend = "SendChatMessageVerifiedAsync(";
        Assert.Equal(
            workflow.IndexOf(verifiedSend, StringComparison.Ordinal),
            workflow.LastIndexOf(verifiedSend, StringComparison.Ordinal));

        Assert.Contains("TryRefreshTabBindingAsync", chrome, StringComparison.Ordinal);
        Assert.Contains("RebindTab(tab, current)", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void SuppliedBrandIconIsUsedByAppSetupWindowAndTray()
    {
        var app = ReadSource("src", "GPTDeskTop", "GPTDeskTop.csproj");
        var setup = ReadSource("src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var tray = ReadSource("src", "GPTDeskTop", "Services", "TrayNotificationService.cs");
        Assert.Contains("<ApplicationIcon>Assets\\GPTDeskTop.ico</ApplicationIcon>", app, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop.ico</ApplicationIcon>", setup, StringComparison.Ordinal);
        Assert.Contains("ExtractAssociatedIcon(Application.ExecutablePath)", main, StringComparison.Ordinal);
        Assert.Contains("ExtractAssociatedIcon(Application.ExecutablePath)", tray, StringComparison.Ordinal);
    }
}
