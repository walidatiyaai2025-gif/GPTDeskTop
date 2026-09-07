using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Runs Monitor Only as the true cold-start application mode. Returning true is the sole
/// authorization for Program to construct any Current GPTDeskTop business/runtime services.
/// </summary>
internal static class MonitorOnlyStartupGate
{
    private const string DelaySetting = "SimpleMonitor.DelaySeconds";

    internal static bool Run(LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        NormalizeLegacyDelaySetting(database);

        using var form = new SimpleMonitorForm(database);
        using var experience = MonitorOnlyExperienceController.Attach(form);
        MonitorOnlyRuntimeInspectorExport.Install(form);
        Application.Run(form);
        return experience.SwitchToCurrentRequested;
    }

    private static void NormalizeLegacyDelaySetting(LocalDatabase database)
    {
        var raw = database.GetSettingAsync(DelaySetting).GetAwaiter().GetResult();
        if (!int.TryParse(raw, out var storedDelay)) return;

        var normalized = SimpleMonitorMessagePlanService.NormalizeRuntimeDelaySeconds(storedDelay);
        if (normalized == storedDelay) return;

        database.SetSettingAsync(DelaySetting, normalized.ToString()).GetAwaiter().GetResult();
    }
}
