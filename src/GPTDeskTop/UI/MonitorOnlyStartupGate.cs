using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.UI;

/// <summary>
/// Runs Monitor Only as the only interactive GPTDeskTop application experience.
/// </summary>
internal static class MonitorOnlyStartupGate
{
    private const string DelaySetting = "SimpleMonitor.DelaySeconds";
    private const string ProfileSetting = "SimpleMonitor.ProfileKey";

    internal static void Run(LocalDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        // T0015 field-regression reconciliation: resolve the saved selected profile before the
        // process-lifetime guard performs its first destructive inventory. A pre-existing managed
        // Chrome is preserved only when this exact profile has a live stable CDP endpoint.
        var savedSelectedProfile = ResolveSavedSelectedProfile(database);

        // Start the lifetime guard before any UI is constructed. It blocks delayed unauthorized
        // GPTDeskTop-managed Chrome while preserving only the verified healthy saved selection.
        MonitorOnlyManagedChromeGuard.Start(savedSelectedProfile);

        // Independently reconcile cold-start residue. Healthy selected Chrome is adopted; stale or
        // competing GPTDeskTop-owned process trees are removed. Ordinary Chrome is never targeted.
        MonitorOnlyColdStartChromeReconciler.ReconcileBeforeIdleUi(savedSelectedProfile);

        NormalizeLegacyDelaySetting(database);

        using var form = new SimpleMonitorForm(database);
        using var experience = MonitorOnlyExperienceController.Attach(form);
        MonitorOnlyHardCutoverUi.Apply(form);
        MonitorOnlyRuntimeInspectorExport.Install(form);
        Application.Run(form);
    }

    private static ChromeProfileInfo? ResolveSavedSelectedProfile(LocalDatabase database)
    {
        var key = database.GetSettingAsync(ProfileSetting).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(key)) return null;

        return ChromeProfileCatalog.Discover().FirstOrDefault(profile =>
            string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase));
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
