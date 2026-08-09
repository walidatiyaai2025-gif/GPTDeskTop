namespace GPTDeskTop.RuntimeTests;

public sealed class MessageCountRotationRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SettingsExposeThresholdAndFixedStartMessage()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("RotateAfterAssistantMessages", source, StringComparison.Ordinal);
        Assert.Contains("MessageCountRotationStartMessage", source, StringComparison.Ordinal);
        Assert.Contains("Rotate after assistant messages (0 = off)", source, StringComparison.Ordinal);
        Assert.Contains("Message-count new Chat start message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorRotatesAfterConfiguredAssistantCount()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.Contains("state.AssistantCount >= rotateAfterMessages", source, StringComparison.Ordinal);
        Assert.Contains("RotateByMessageCountAsync", source, StringComparison.Ordinal);
        Assert.Contains("\"AssistantMessageCount\"", source, StringComparison.Ordinal);
        Assert.Contains("\"RotatedByMessageCount\"", source, StringComparison.Ordinal);
        Assert.Contains("\"MessageCountRotationStartSent\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageCountRotationPreservesVerifiedHandoffAndMonitorIdentity()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var helper = source.IndexOf("private async Task<ChromeTab?> RotateByMessageCountAsync", StringComparison.Ordinal);
        var verifiedSend = source.IndexOf("SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true", helper, StringComparison.Ordinal);
        var increment = source.IndexOf("monitor.RotationCount++", helper, StringComparison.Ordinal);
        var save = source.IndexOf("await _database.SaveMonitorAsync(monitor", increment, StringComparison.Ordinal);
        var closeOld = source.IndexOf("await _chrome.CloseTabAsync(oldTab", save, StringComparison.Ordinal);

        Assert.True(helper >= 0);
        Assert.True(verifiedSend > helper);
        Assert.True(increment > verifiedSend);
        Assert.True(save > increment);
        Assert.True(closeOld > save);
    }
}
