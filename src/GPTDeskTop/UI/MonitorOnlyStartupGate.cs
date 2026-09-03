using GPTDeskTop.Data;

namespace GPTDeskTop.UI;

/// <summary>
/// Runs Monitor Only as the true cold-start application mode. Returning true is the sole
/// authorization for Program to construct any Current GPTDeskTop business/runtime services.
/// </summary>
internal static class MonitorOnlyStartupGate
{
    internal static bool Run(LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        using var form = new SimpleMonitorForm(database);
        using var experience = MonitorOnlyExperienceController.Attach(form);
        Application.Run(form);
        return experience.SwitchToCurrentRequested;
    }
}
