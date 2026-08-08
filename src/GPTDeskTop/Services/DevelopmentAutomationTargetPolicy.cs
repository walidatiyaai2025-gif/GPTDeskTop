using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Defines the hard boundary for Development Automation targets.
/// A monitor must be saved, enabled, have a tab id, and have an explicit
/// Development Automation opt-in before it can become a target.
/// </summary>
public static class DevelopmentAutomationTargetPolicy
{
    public static bool IsEligible(SavedMonitor monitor, bool optedIn)
        => monitor.Enabled
           && !string.IsNullOrWhiteSpace(monitor.TabId)
           && optedIn;
}
