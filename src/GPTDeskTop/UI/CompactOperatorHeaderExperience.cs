using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

/// <summary>
/// Compatibility bootstrap retained for older binaries/source references. Physical header sizing
/// is intentionally delegated to CompactDashboardHeaderLayout so there is exactly one DPI-aware
/// owner and no delayed secondary layout pass can restore legacy 58px chrome.
/// </summary>
internal static class CompactOperatorHeaderExperienceBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => CompactDashboardHeaderLayout.ApplyOpenForms();
}

internal static class CompactOperatorHeaderExperience
{
    internal static bool Apply(Form form)
        => CompactDashboardHeaderLayout.Apply(form);
}
