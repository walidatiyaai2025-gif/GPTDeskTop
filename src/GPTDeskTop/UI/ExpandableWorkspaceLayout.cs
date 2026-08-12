using System.Runtime.CompilerServices;

namespace GPTDeskTop.UI;

internal static class ExpandableWorkspaceLayoutBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Application.Idle += (_, _) => ExpandableWorkspaceLayout.ApplyOpenForms();
}

/// <summary>
/// Owns the DPI-scaled physical heights of expandable dashboard workspaces.
/// Controls keep ownership of their expanded/collapsed state; this layout layer is the
/// single place that translates that state into physical height, including compact operator mode.
/// Registration is incremental: each form/control tree is visited once and late controls are
/// registered through ControlAdded.
/// </summary>
internal static class ExpandableWorkspaceLayout
{
    private const int DevelopmentCollapsedHeight = 72;
    private const int DevelopmentExpandedHeight = 178;
    private const int CompactDevelopmentCollapsedHeight = 58;
    private const int CompactDevelopmentExpandedHeight = 118;
    private const int RuntimeHealthCollapsedHeight = 62;
    private const int RuntimeHealthExpandedHeight = 188;
    private const int CompactRuntimeHealthCollapsedHeight = 58;
    private const int CompactRuntimeHealthExpandedHeight = 140;
    private const int HistoryCollapsedHeight = 56;
    private const int HistoryExpandedHeight = 330;

    private static readonly ConditionalWeakTable<Form, FormRegistration> Forms = new();
    private static readonly ConditionalWeakTable<Control, ControlRegistration> Controls = new();
    private static readonly ConditionalWeakTable<Control, CompactOperatorRegistration> CompactOperatorControls = new();

    internal static void ApplyOpenForms()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            Apply(form);
    }

    internal static void Apply(Form form)
    {
        if (form.IsDisposed || form.Disposing) return;

        var registration = Forms.GetValue(form, _ => new FormRegistration());
        if (registration.Initialized) return;
        registration.Initialized = true;

        RegisterTree(form);
    }

    /// <summary>
    /// Enables compact physical heights for a supported expandable operator control while keeping
    /// legacy heights unchanged everywhere else. CompactTopCommandMenuExperience calls this after
    /// it removes the embedded action chrome.
    /// </summary>
    internal static void EnableCompactOperatorLayout(Control control)
    {
        if (control.IsDisposed || control.Disposing) return;

        CompactOperatorControls.GetValue(control, _ => new CompactOperatorRegistration());
        RegisterTree(control);
        ApplyExpandableHeight(control);
    }

    private static void RegisterTree(Control control)
    {
        if (control.IsDisposed || control.Disposing) return;

        var registration = Controls.GetValue(control, _ => new ControlRegistration());
        if (!registration.ChildHooked)
        {
            registration.ChildHooked = true;
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is not null && !e.Control.IsDisposed)
                    RegisterTree(e.Control);
            };
        }

        RegisterExpandable(control, registration);

        foreach (Control child in control.Controls)
            RegisterTree(child);
    }

    private static void RegisterExpandable(Control control, ControlRegistration registration)
    {
        if (registration.ExpandableHooked) return;
        if (!IsSupportedExpandable(control)) return;

        registration.ExpandableHooked = true;

        void ApplyCurrentHeight()
        {
            if (control.IsDisposed || control.Disposing) return;
            ApplyExpandableHeight(control);
        }

        control.SizeChanged += (_, _) => ApplyCurrentHeight();
        control.DpiChangedAfterParent += (_, _) => ApplyCurrentHeight();
        ApplyCurrentHeight();
    }

    private static bool IsSupportedExpandable(Control control)
        => control is DevelopmentTaskDashboardControl or RuntimeHealthControl or HistoryWorkspaceControl;

    private static void ApplyExpandableHeight(Control control)
    {
        var compact = CompactOperatorControls.TryGetValue(control, out _);

        switch (control)
        {
            case DevelopmentTaskDashboardControl development:
                ApplyHeight(
                    development,
                    development.IsExpanded,
                    compact ? CompactDevelopmentCollapsedHeight : DevelopmentCollapsedHeight,
                    compact ? CompactDevelopmentExpandedHeight : DevelopmentExpandedHeight);
                break;
            case RuntimeHealthControl runtimeHealth:
                ApplyHeight(
                    runtimeHealth,
                    runtimeHealth.IsExpanded,
                    compact ? CompactRuntimeHealthCollapsedHeight : RuntimeHealthCollapsedHeight,
                    compact ? CompactRuntimeHealthExpandedHeight : RuntimeHealthExpandedHeight);
                break;
            case HistoryWorkspaceControl history:
                ApplyHeight(history, history.IsExpanded, HistoryCollapsedHeight, HistoryExpandedHeight);
                break;
        }
    }

    private static void ApplyHeight(
        Control control,
        bool isExpanded,
        int collapsedLogicalHeight,
        int expandedLogicalHeight)
    {
        var minimumHeight = Scale(control, collapsedLogicalHeight);
        var expectedHeight = Scale(control, isExpanded ? expandedLogicalHeight : collapsedLogicalHeight);

        if (control.MinimumSize.Height != minimumHeight)
            control.MinimumSize = new Size(control.MinimumSize.Width, minimumHeight);

        if (control.Height != expectedHeight)
            control.Height = expectedHeight;
    }

    internal static int Scale(Control control, int logicalPixels)
        => Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, control.DeviceDpi) / 96d));

    private sealed class FormRegistration
    {
        internal bool Initialized { get; set; }
    }

    private sealed class ControlRegistration
    {
        internal bool ChildHooked { get; set; }
        internal bool ExpandableHooked { get; set; }
    }

    private sealed class CompactOperatorRegistration
    {
    }
}
