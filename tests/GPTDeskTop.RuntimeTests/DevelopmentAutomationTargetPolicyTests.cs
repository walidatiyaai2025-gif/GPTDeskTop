using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentAutomationTargetPolicyTests
{
    [Fact]
    public void EnabledOptedInMonitorWithTabIsEligible()
    {
        var monitor = new SavedMonitor
        {
            Enabled = true,
            TabId = "tab-1"
        };

        Assert.True(DevelopmentAutomationTargetPolicy.IsEligible(monitor, optedIn: true));
    }

    [Theory]
    [InlineData(false, "tab-1", true)]
    [InlineData(true, "", true)]
    [InlineData(true, "   ", true)]
    [InlineData(true, "tab-1", false)]
    public void NonEligibleMonitorIsNeverAnAutomationTarget(bool enabled, string tabId, bool optedIn)
    {
        var monitor = new SavedMonitor
        {
            Enabled = enabled,
            TabId = tabId
        };

        Assert.False(DevelopmentAutomationTargetPolicy.IsEligible(monitor, optedIn));
    }

    [Fact]
    public void OptInDoesNotOverrideDisabledMonitor()
    {
        var monitor = new SavedMonitor
        {
            Enabled = false,
            TabId = "tab-1"
        };

        Assert.False(DevelopmentAutomationTargetPolicy.IsEligible(monitor, optedIn: true));
    }
}
