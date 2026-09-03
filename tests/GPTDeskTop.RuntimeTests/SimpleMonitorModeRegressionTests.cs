using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorModeRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("https://chatgpt.com/c/abc123", "abc123")]
    [InlineData("https://chatgpt.com/c/abc123?model=auto", "abc123")]
    [InlineData("https://CHATGPT.com/c/abc123/", "abc123")]
    public void StableConversationIdentityIsRequired(string url, string expected)
    {
        Assert.True(SimpleMonitorProfileSession.TryGetConversationId(url, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(SimpleMonitorProfileSession.TryGetConversationId("https://chatgpt.com/", out _));
        Assert.False(SimpleMonitorProfileSession.TryGetConversationId("https://example.com/c/abc123", out _));
    }

    [Fact]
    public void AlternateFormExposesOnlyTheRequestedSimpleMonitorBusiness()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");

        Assert.Contains("Monitor Only — Same Chat", source, StringComparison.Ordinal);
        Assert.Contains("Chrome profile", source, StringComparison.Ordinal);
        Assert.Contains("Same Chat = ON", source, StringComparison.Ordinal);
        Assert.Contains("New Chat = OFF", source, StringComparison.Ordinal);
        Assert.Contains("Rotation = OFF", source, StringComparison.Ordinal);
        Assert.Contains("Minimum = 15", source, StringComparison.Ordinal);
        Assert.Contains("Stored message sequence", source, StringComparison.Ordinal);
        Assert.Contains("Load JSON Plan", source, StringComparison.Ordinal);
        Assert.Contains("Download Sample JSON", source, StringComparison.Ordinal);
        Assert.Contains("Copy ChatGPT Prompt", source, StringComparison.Ordinal);
        Assert.Contains("Preview / Validate", source, StringComparison.Ordinal);
        Assert.Contains("Runtime Inspector", source, StringComparison.Ordinal);
        Assert.Contains("DrawMode = DrawMode.OwnerDrawFixed", source, StringComparison.Ordinal);
        Assert.Contains("CheckpointPlanMessageSentAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (!loop)", runner, StringComparison.Ordinal);
        Assert.Contains("messageIndex = 0", runner, StringComparison.Ordinal);
        Assert.Contains("messageIndex++", runner, StringComparison.Ordinal);
        Assert.Contains("Runtime.evaluate timeout", runner, StringComparison.Ordinal);
        Assert.Contains("Step.EffectiveDelaySeconds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeNeverCreatesOrRotatesToAnotherChat()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");

        Assert.DoesNotContain("CreateNewChatTabAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversationRotation", runner, StringComparison.Ordinal);
        Assert.Contains("openIfMissing: false", runner, StringComparison.Ordinal);
        Assert.Contains("No other chat will be used", runner, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorVerifiedSender.SendOnceAndVerifyAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("session.Chrome.SendChatMessageVerifiedAsync(", runner, StringComparison.Ordinal);
        Assert.Contains("ReadChatStateCoreAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("GetChatStateAsync(", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyCompletesAllRecoveryGatesBeforeComposerMutationAndNeverRefreshesAfterLoad()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var sender = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorVerifiedSender.cs");

        var sendGateIndex = runner.IndexOf("AcquireSendPermitAsync", StringComparison.Ordinal);
        var senderIndex = runner.IndexOf("SimpleMonitorVerifiedSender.SendOnceAndVerifyAsync", StringComparison.Ordinal);
        Assert.True(sendGateIndex >= 0);
        Assert.True(senderIndex > sendGateIndex);

        Assert.Contains("SendChatMessageAsync(tab, message", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("chrome.SendChatMessageVerifiedAsync(", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync(", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshStuckComposerAsync(", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRefreshTabBindingAsync(", sender, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveConversationAsync(", sender, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorSendUncertainException", sender, StringComparison.Ordinal);
        Assert.Contains("Automatic retry is blocked", sender, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeSwitchStopsClassicSavedMonitorsBeforeHidingCurrentUi()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorModeBootstrap.cs");

        Assert.Contains("await monitor.StopAllAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceDesiredMonitorIdsAsync(database, Array.Empty<long>())", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("await monitor.StopAllAsync()", StringComparison.Ordinal)
            < source.IndexOf("main.Hide()", StringComparison.Ordinal));
        Assert.Contains("MonitorOnlyExperienceController.Attach(_monitorOnlyForm)", source, StringComparison.Ordinal);
        Assert.Contains("Current GPTDeskTop", source, StringComparison.Ordinal);
        Assert.Contains("Monitor Only — Same Chat", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyColdStartBlocksLegacyBusinessUntilCurrentRadioSelected()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");
        var gate = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");
        var experience = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyExperienceController.cs");

        var gateIndex = program.IndexOf("MonitorOnlyStartupGate.Run(database)", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0);
        Assert.True(gateIndex < program.IndexOf("CrashRecoveryStateService.PrepareStartupAsync", StringComparison.Ordinal));
        Assert.True(gateIndex < program.IndexOf("new ChromeDevToolsService", StringComparison.Ordinal));
        Assert.True(gateIndex < program.IndexOf("new ChatGptMonitorService", StringComparison.Ordinal));
        Assert.True(gateIndex < program.IndexOf("new DevelopmentTaskRuntimeBinding", StringComparison.Ordinal));
        Assert.DoesNotContain("MonitorOnlyStartupCoordinator.Prepare", program, StringComparison.Ordinal);

        Assert.Contains("Application.Run(form)", gate, StringComparison.Ordinal);
        Assert.Contains("return experience.SwitchToCurrentRequested", gate, StringComparison.Ordinal);
        Assert.Contains("SwitchToCurrentRequested = _currentModeRadio.Checked", experience, StringComparison.Ordinal);
        Assert.Contains("Closing the window with X/Alt+F4", experience, StringComparison.Ordinal);
    }

    [Fact]
    public void PremiumMonitorOnlyCompositionMatchesRequestedLayoutAndStreamsFooterLive()
    {
        var experience = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyExperienceController.cs");

        Assert.Contains("topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50))", experience, StringComparison.Ordinal);
        Assert.Contains("topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23))", experience, StringComparison.Ordinal);
        Assert.Contains("topCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27))", experience, StringComparison.Ordinal);
        Assert.Contains("Monitor the same chat until assistant response is complete.", experience, StringComparison.Ordinal);
        Assert.Contains("LIVE CHAT", experience, StringComparison.Ordinal);
        Assert.Contains("Interval = 1500", experience, StringComparison.Ordinal);
        Assert.Contains("ReadChatStateCoreAsync", experience, StringComparison.Ordinal);
        Assert.Contains("state.LastAssistantText", experience, StringComparison.Ordinal);
        Assert.Contains("openIfMissing: false", experience, StringComparison.Ordinal);
        Assert.Contains("Live stream temporarily unavailable", experience, StringComparison.Ordinal);
        Assert.Contains("must never change Monitor", experience, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveMessageSplitterResetsConstraintsBeforeOrientationChange()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");

        Assert.Contains("_messageSplit.Panel1MinSize = 0", source, StringComparison.Ordinal);
        Assert.Contains("_messageSplit.Panel2MinSize = 0", source, StringComparison.Ordinal);
        Assert.Contains("_messageSplit.SplitterDistance = 0", source, StringComparison.Ordinal);
        Assert.Contains("if (_messageSplit.Orientation != targetOrientation)", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(desiredDistance, 1, available - 1)", source, StringComparison.Ordinal);
        Assert.Contains("ResetSplitMinimums()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Max(_messageSplit.Panel1MinSize, _messageSplit.Height / 2)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StoredAndJsonMessagesCanBeDeletedAndDeletionIsDurable()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");

        Assert.Contains("Delete Selected", source, StringComparison.Ordinal);
        Assert.Contains("await RemoveMessageAsync()", source, StringComparison.Ordinal);
        Assert.Contains("_loadedPlan.Messages.RemoveAt(index)", source, StringComparison.Ordinal);
        Assert.Contains("SetSettingAsync(PlanSetting, SimpleMonitorMessagePlanService.Serialize(_loadedPlan))", source, StringComparison.Ordinal);
        Assert.Contains("PersistManualMessagesAsync", source, StringComparison.Ordinal);
        Assert.Contains("_removeMessageButton.Enabled = canDelete", source, StringComparison.Ordinal);
        Assert.Contains("savedMessagesJson is null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_loadedPlan is not null) return;\n        var index = _messagesList.SelectedIndex;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedChromeProfileGetsASeparatePersistentAutomationSafeSession()
    {
        var catalog = ReadSource("src", "GPTDeskTop", "Services", "ChromeProfileCatalog.cs");
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");

        Assert.Contains("Local State", catalog, StringComparison.Ordinal);
        Assert.Contains("info_cache", catalog, StringComparison.Ordinal);
        Assert.Contains("ChromeProfiles", catalog, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-port=", session, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", session, StringComparison.Ordinal);
        Assert.Contains("Profile.ManagedUserDataDirectory", session, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductVersionIsBumpedToTwoPointZeroPointTwentyFour()
    {
        var props = ReadSource("Directory.Build.props");
        Assert.Contains("<GPTDeskTopVersion>2.0.24</GPTDeskTopVersion>", props, StringComparison.Ordinal);
    }
}