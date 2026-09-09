using GPTDeskTop.Data;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Runs Monitor Only as the only interactive GPTDeskTop application experience.
/// </summary>
internal static class MonitorOnlyStartupGate
{
    private const string DelaySetting = "SimpleMonitor.DelaySeconds";

    internal static void Run(LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        // Start the process-level idle guard before any UI is constructed. It watches for delayed
        // GPTDeskTop-managed Chrome starts throughout the app lifetime; ordinary user Chrome is
        // outside the ownership filter.
        MonitorOnlyManagedChromeGuard.Start();

        // Keep the synchronous cold-start reconciliation as an independent fail-closed gate for
        // residue that already existed before this process started.
        MonitorOnlyColdStartChromeReconciler.ReconcileBeforeIdleUi();

        NormalizeLegacyDelaySetting(database);

        using var form = new SimpleMonitorForm(database);
        using var experience = MonitorOnlyExperienceController.Attach(form);
        MonitorOnlyHardCutoverUi.Apply(form);
        MonitorOnlyRuntimeInspectorExport.Install(form);
        Application.Run(form);
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
