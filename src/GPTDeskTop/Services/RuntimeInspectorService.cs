using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services;

internal sealed record RuntimeInspectorBrowserProcess(
    int ProcessId,
    string Name,
    string MainWindowTitle,
    bool Responding,
    bool HasMainWindow);

internal sealed record BrowserProcessDiagnostics(
    string Scope,
    string OwnershipNote,
    int Total,
    int Chrome,
    int EdgeOrWebView,
    int TitledWindows,
    int Responding);

internal sealed record RuntimeInspectorComposerDiagnostics(
    string Decision,
    string Reason,
    DateTimeOffset ObservedAtUtc,
    double AgeSeconds,
    bool IsStale);

internal sealed record RuntimeInspectorVerifiedSendDiagnostics(
    string Phase,
    string Reason,
    int SubmitAttempts,
    DateTimeOffset ObservedAtUtc,
    double AgeSeconds,
    bool IsStale);

internal sealed record RuntimeInspectorUiOverflow(
    string FormScope,
    string ParentType,
    string ChildType,
    int Left,
    int Top,
    int Right,
    int Bottom);

internal sealed record RuntimeInspectorUiDiagnostics(
    int FormsCaptured,
    int ControlsCaptured,
    int VisibleControls,
    int VisibleOverflowCount,
    IReadOnlyList<RuntimeInspectorUiOverflow> VisibleOverflows);

internal sealed record FieldRuntimeSnapshot(
    DateTimeOffset CapturedUtc,
    object Build,
    IReadOnlyList<object> Monitors,
    IReadOnlyList<object> Browsers,
    BrowserProcessDiagnostics BrowserDiagnostics,
    RuntimeInspectorComposerDiagnostics ComposerDiagnostics,
    RuntimeInspectorVerifiedSendDiagnostics VerifiedSendDiagnostics,
    RuntimeInspectorUiDiagnostics UiDiagnostics,
    RuntimeFlightSnapshot FlightRecorder,
    IReadOnlyList<object> Ui,
    IReadOnlyList<object> Workers);

internal static class RuntimeInspectorService
{
    private const int MaxOverflowRows = 25;
    private const int OverflowToleranceLogicalPixels = 2;
    private static readonly TimeSpan DiagnosticStaleAfter = TimeSpan.FromMinutes(5);

    public static FieldRuntimeSnapshot Capture(Form owner, ChatGptMonitorService monitor)
    {
        var capturedUtc = DateTimeOffset.UtcNow;
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var exe = Environment.ProcessPath ?? assembly.Location;
        var build = new
        {
            ProductVersion = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "unknown",
            AssemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown",
            ExecutablePath = exe,
            ProcessId = Environment.ProcessId,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString()
        };

        var monitors = CaptureMonitorRuntime(monitor);
        var browserRows = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
            .Select(SafeProcess)
            .Where(x => x is not null)
            .Cast<RuntimeInspectorBrowserProcess>()
            .ToArray();
        var browsers = browserRows.Cast<object>().ToArray();
        var browserDiagnostics = new BrowserProcessDiagnostics(
            Scope: "System-wide",
            OwnershipNote: "Diagnostic inventory only; these processes are not asserted to be owned by GPTDeskTop.",
            Total: browserRows.Length,
            Chrome: browserRows.Count(row => row.Name.Contains("chrome", StringComparison.OrdinalIgnoreCase)),
            EdgeOrWebView: browserRows.Count(row => row.Name.Contains("msedge", StringComparison.OrdinalIgnoreCase) || row.Name.Contains("webview", StringComparison.OrdinalIgnoreCase)),
            TitledWindows: browserRows.Count(row => row.HasMainWindow || !string.IsNullOrWhiteSpace(row.MainWindowTitle)),
            Responding: browserRows.Count(row => row.Responding));

        var composerSnapshot = ChatComposerDecisionDiagnostics.Last;
        var composerAgeSeconds = Math.Max(0, (capturedUtc - composerSnapshot.ObservedAtUtc).TotalSeconds);
        var composerDiagnostics = new RuntimeInspectorComposerDiagnostics(
            composerSnapshot.Decision.ToString(),
            composerSnapshot.Reason,
            composerSnapshot.ObservedAtUtc,
            composerAgeSeconds,
            composerAgeSeconds > DiagnosticStaleAfter.TotalSeconds);
        var verifiedSendSnapshot = VerifiedSendDiagnostics.Last;
        var verifiedSendAgeSeconds = Math.Max(0, (capturedUtc - verifiedSendSnapshot.ObservedAtUtc).TotalSeconds);
        var verifiedSendDiagnostics = new RuntimeInspectorVerifiedSendDiagnostics(
            verifiedSendSnapshot.Phase,
            verifiedSendSnapshot.Reason,
            verifiedSendSnapshot.SubmitAttempts,
            verifiedSendSnapshot.ObservedAtUtc,
            verifiedSendAgeSeconds,
            verifiedSendAgeSeconds > DiagnosticStaleAfter.TotalSeconds);

        var ui = new List<object>();
        var overflows = new List<RuntimeInspectorUiOverflow>();
        var controlsCaptured = 0;
        var visibleControls = 0;
        var forms = ResolveForms(owner);
        for (var index = 0; index < forms.Count; index++)
        {
            var form = forms[index];
            var formScope = ReferenceEquals(form, owner)
                ? "MainForm"
                : $"AuxiliaryForm#{index}:{form.GetType().Name}";
            Walk(form, formScope, 0, ui, overflows, ref controlsCaptured, ref visibleControls);
            WalkToolStrips(form, formScope, ui);
        }

        var uiDiagnostics = new RuntimeInspectorUiDiagnostics(
            FormsCaptured: forms.Count,
            ControlsCaptured: controlsCaptured,
            VisibleControls: visibleControls,
            VisibleOverflowCount: overflows.Count,
            VisibleOverflows: overflows.Take(MaxOverflowRows).ToArray());
        var flightRecorder = RuntimeFlightRecorder.Snapshot();

        var workers = monitors.Select(m => (object)new { Kind = "MonitorWorker", Snapshot = m }).ToArray();
        return new FieldRuntimeSnapshot(
            capturedUtc,
            build,
            monitors,
            browsers,
            browserDiagnostics,
            composerDiagnostics,
            verifiedSendDiagnostics,
            uiDiagnostics,
            flightRecorder,
            ui,
            workers);
    }

    public static string ToSanitizedJson(FieldRuntimeSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

    public static string Summary(FieldRuntimeSnapshot snapshot)
    {
        var build = JsonSerializer.Serialize(snapshot.Build);
        var browser = snapshot.BrowserDiagnostics;
        var composer = snapshot.ComposerDiagnostics;
        var verifiedSend = snapshot.VerifiedSendDiagnostics;
        var ui = snapshot.UiDiagnostics;
        var flight = snapshot.FlightRecorder;
        var flightMonitors = flight.MonitorCounts.Count == 0
            ? "none"
            : string.Join(",", flight.MonitorCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
        return $"GPTDeskTop Runtime Inspector\r\n" +
               $"Captured: {snapshot.CapturedUtc:O}\r\n" +
               $"Build: {build}\r\n" +
               $"Monitors: {snapshot.Monitors.Count}\r\n" +
               $"System browser processes: {browser.Total} (Chrome: {browser.Chrome}, Edge/WebView: {browser.EdgeOrWebView}, titled windows: {browser.TitledWindows})\r\n" +
               $"Browser scope: {browser.Scope} — {browser.OwnershipNote}\r\n" +
               $"Composer gate: {composer.Reason} ({composer.Decision}) @ {composer.ObservedAtUtc:O} | age: {composer.AgeSeconds:0}s | stale: {(composer.IsStale ? "yes" : "no")}\r\n" +
               $"Verified send: {verifiedSend.Phase} | attempts: {verifiedSend.SubmitAttempts} | {verifiedSend.Reason} @ {verifiedSend.ObservedAtUtc:O} | age: {verifiedSend.AgeSeconds:0}s | stale: {(verifiedSend.IsStale ? "yes" : "no")}\r\n" +
               $"Flight recorder: {flight.EventCount}/{flight.Capacity} events | seq {flight.FirstSequence}-{flight.LastSequence} | monitors {flightMonitors}\r\n" +
               $"UI forms: {ui.FormsCaptured} | visible controls: {ui.VisibleControls} | visible overflows: {ui.VisibleOverflowCount}\r\n" +
               $"UI controls: {snapshot.Ui.Count}\r\n";
    }

    public static string ExportBundle(Form owner, ChatGptMonitorService monitor, string destinationZip)
    {
        var snapshot = Capture(owner, monitor);
        var temp = Path.Combine(Path.GetTempPath(), "GPTDeskTop-Field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "runtime-inspector.json"), ToSanitizedJson(snapshot), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temp, "runtime-flight-recorder.json"), JsonSerializer.Serialize(snapshot.FlightRecorder, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temp, "summary.txt"), Summary(snapshot), new UTF8Encoding(false));
            var appDir = AppContext.BaseDirectory;
            var candidateLogs = Directory.EnumerateFiles(appDir, "*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(3)
                .ToArray();
            foreach (var log in candidateLogs)
            {
                var lines = File.ReadLines(log).TakeLast(1000);
                File.WriteAllLines(Path.Combine(temp, "log-" + Path.GetFileName(log)), Redact(lines), new UTF8Encoding(false));
            }
            if (File.Exists(destinationZip)) File.Delete(destinationZip);
            ZipFile.CreateFromDirectory(temp, destinationZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            return destinationZip;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static IReadOnlyList<Form> ResolveForms(Form owner)
    {
        var forms = new List<Form>();
        var seen = new HashSet<Form>(ReferenceEqualityComparer.Instance);

        void Add(Form? form)
        {
            if (form is null || form.IsDisposed || form.Disposing || !seen.Add(form)) return;
            forms.Add(form);
        }

        Add(owner);
        foreach (var owned in owner.OwnedForms)
            Add(owned);

        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (ReferenceEquals(form, owner) || ReferenceEquals(form.Owner, owner))
                Add(form);
        }

        return forms;
    }

    private static IReadOnlyList<object> CaptureMonitorRuntime(ChatGptMonitorService monitor)
    {
        return MonitorRuntimeDiagnosticReader.Capture(monitor)
            .Select(diagnostic => (object)new
            {
                diagnostic.MonitorId,
                // Compatibility field: WorkerStatus used to expose Task.Status directly. It now
                // carries the semantic monitor lifecycle so field tooling does not mistake a
                // normal async WaitingForActivation state for a failed monitor start.
                WorkerStatus = diagnostic.LifecycleStatus,
                diagnostic.LifecycleStatus,
                diagnostic.RawTaskStatus,
                diagnostic.IsCompleted,
                diagnostic.IsFaulted,
                diagnostic.CancellationRequested,
                diagnostic.ObservedSinceUtc,
                diagnostic.ObservedForSeconds,
                CapturedUtc = DateTimeOffset.UtcNow
            })
            .ToArray();
    }

    private static RuntimeInspectorBrowserProcess? SafeProcess(Process process)
    {
        try
        {
            var title = process.MainWindowTitle;
            return new RuntimeInspectorBrowserProcess(
                process.Id,
                process.ProcessName,
                title,
                process.Responding,
                process.MainWindowHandle != IntPtr.Zero);
        }
        catch
        {
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void Walk(
        Control control,
        string formScope,
        int depth,
        List<object> output,
        List<RuntimeInspectorUiOverflow> overflows,
        ref int controlsCaptured,
        ref int visibleControls)
    {
        controlsCaptured++;
        if (control.Visible) visibleControls++;

        output.Add(new
        {
            Kind = "Control",
            FormScope = formScope,
            control.Name,
            Type = control.GetType().FullName,
            control.Visible,
            control.Enabled,
            Bounds = new { control.Bounds.X, control.Bounds.Y, control.Bounds.Width, control.Bounds.Height },
            Dpi = control.DeviceDpi,
            Depth = depth
        });

        foreach (Control child in control.Controls)
        {
            CaptureVisibleOverflow(control, child, formScope, overflows);
            Walk(child, formScope, depth + 1, output, overflows, ref controlsCaptured, ref visibleControls);
        }
    }

    private static void CaptureVisibleOverflow(
        Control parent,
        Control child,
        string formScope,
        List<RuntimeInspectorUiOverflow> output)
    {
        if (!parent.Visible || !child.Visible)
            return;
        if (parent is ScrollableControl scrollable && scrollable.AutoScroll)
            return;

        var bounds = child.Bounds;
        var client = parent.ClientSize;
        if (client.Width <= 0 || client.Height <= 0)
            return;

        var tolerance = Math.Max(
            OverflowToleranceLogicalPixels,
            (int)Math.Round(OverflowToleranceLogicalPixels * Math.Max(96, parent.DeviceDpi) / 96d));
        var left = Math.Max(0, -bounds.Left);
        var top = Math.Max(0, -bounds.Top);
        var right = Math.Max(0, bounds.Right - client.Width);
        var bottom = Math.Max(0, bounds.Bottom - client.Height);
        if (left <= tolerance && top <= tolerance && right <= tolerance && bottom <= tolerance)
            return;

        output.Add(new RuntimeInspectorUiOverflow(
            formScope,
            parent.GetType().FullName ?? parent.GetType().Name,
            child.GetType().FullName ?? child.GetType().Name,
            left,
            top,
            right,
            bottom));
    }

    private static void WalkToolStrips(Control root, string formScope, List<object> output)
    {
        foreach (var strip in DescendantsAndSelf(root).OfType<ToolStrip>())
        {
            foreach (ToolStripItem item in strip.Items)
                WalkToolStripItem(item, formScope, depth: 0, output);
        }
    }

    private static void WalkToolStripItem(ToolStripItem item, string formScope, int depth, List<object> output)
    {
        var owner = item.Owner;
        var bounds = item.Bounds;
        output.Add(new
        {
            Kind = "ToolStripItem",
            FormScope = formScope,
            item.Name,
            item.Text,
            Type = item.GetType().FullName,
            item.Visible,
            item.Available,
            item.Enabled,
            Bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height },
            OwnerType = owner?.GetType().FullName,
            Dpi = owner?.DeviceDpi ?? 96,
            Depth = depth
        });

        if (item is not ToolStripDropDownItem dropDown) return;
        foreach (ToolStripItem child in dropDown.DropDownItems)
            WalkToolStripItem(child, formScope, depth + 1, output);
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static IEnumerable<string> Redact(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("github_pat_", StringComparison.OrdinalIgnoreCase) || line.Contains("Authorization:", StringComparison.OrdinalIgnoreCase) || line.Contains("cookie", StringComparison.OrdinalIgnoreCase))
                yield return "[REDACTED SENSITIVE LINE]";
            else
                yield return line.Length > 4000 ? line[..4000] + " [TRUNCATED]" : line;
        }
    }
}
