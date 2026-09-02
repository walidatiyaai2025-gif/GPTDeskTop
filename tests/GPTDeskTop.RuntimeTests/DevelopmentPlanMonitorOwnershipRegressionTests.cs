using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentPlanMonitorOwnershipRegressionTests
{
    private static string Source(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", Path.Combine(parts))));

    [Fact]
    public void LegacyMonitorStartIsSuppressedWhenDevelopmentMessagesOwnConversation()
    {
        var source = Source("Services", "ChatGptMonitorService.cs");
        Assert.Contains("DevelopmentPlanMonitorSettings.IsEnabledAsync", source, StringComparison.Ordinal);
        Assert.Contains("DevelopmentMonitorLegacyStartSuppressed", source, StringComparison.Ordinal);
        Assert.Contains("Legacy single AutoReply start was suppressed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorConfigurationSavePersistsDevelopmentOwnership()
    {
        var service = Source("Services", "ChatGptMonitorService.cs");
        var main = Source("UI", "MainForm.cs");
        Assert.Contains("DevelopmentPlanMonitorSettings.SetEnabledAsync(_database, monitor, monitor.UseDevelopmentMessages)", service, StringComparison.Ordinal);
        Assert.Contains("DevelopmentPlanMonitorSettings.SetEnabledAsync(_database, monitor, monitor.UseDevelopmentMessages)", main, StringComparison.Ordinal);
    }
}
