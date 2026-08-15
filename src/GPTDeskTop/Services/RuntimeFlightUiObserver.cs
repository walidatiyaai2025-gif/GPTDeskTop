using System.Runtime.CompilerServices;

namespace GPTDeskTop.Services;

/// <summary>
/// Process-wide, privacy-safe UI instrumentation. It records control type/name identity only;
/// user-visible text, editor values and other entered content are deliberately never read.
/// </summary>
internal static class RuntimeFlightUiObserver
{
    private static readonly ConditionalWeakTable<Control, object> HookedControls = new();
    private static readonly object Marker = new();
    private static int _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        Application.Idle += DiscoverOpenForms;
    }

    private static void DiscoverOpenForms(object? sender, EventArgs e)
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            HookControlTree(form);
    }

    private static void HookControlTree(Control control)
    {
        if (!TryMarkHooked(control)) return;

        control.Click += OnControlClick;
        control.ControlAdded += OnControlAdded;

        if (control is Form form)
        {
            RuntimeFlightRecorder.Record("UI", "FormObserved", "open", SafeControlIdentity(form));
            form.Shown += OnFormShown;
            form.VisibleChanged += OnFormVisibleChanged;
            form.FormClosed += OnFormClosed;
        }

        if (control is ToolStrip strip)
            strip.ItemClicked += OnToolStripItemClicked;

        foreach (Control child in control.Controls)
            HookControlTree(child);
    }

    private static bool TryMarkHooked(Control control)
    {
        try
        {
            HookedControls.Add(control, Marker);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
        => HookControlTree(e.Control);

    private static void OnControlClick(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;
        RuntimeFlightRecorder.Record("UI", "Click", "observed", SafeControlIdentity(control));
    }

    private static void OnToolStripItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        var item = e.ClickedItem;
        var identity = string.IsNullOrWhiteSpace(item.Name)
            ? item.GetType().Name
            : $"{item.GetType().Name}:{RuntimeFlightRecorder.SafeIdentity(item.Name)}";
        RuntimeFlightRecorder.Record("UI", "ToolStripClick", "observed", identity);
    }

    private static void OnFormShown(object? sender, EventArgs e)
    {
        if (sender is Form form)
            RuntimeFlightRecorder.Record("UI", "FormShown", "visible", SafeControlIdentity(form));
    }

    private static void OnFormVisibleChanged(object? sender, EventArgs e)
    {
        if (sender is Form form)
            RuntimeFlightRecorder.Record("UI", "FormVisibility", form.Visible ? "visible" : "hidden", SafeControlIdentity(form));
    }

    private static void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is Form form)
            RuntimeFlightRecorder.Record("UI", "FormClosed", "closed", SafeControlIdentity(form));
    }

    private static string SafeControlIdentity(Control control)
        => string.IsNullOrWhiteSpace(control.Name)
            ? control.GetType().Name
            : $"{control.GetType().Name}:{RuntimeFlightRecorder.SafeIdentity(control.Name)}";
}
