using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GPTDeskTop.Runtime;

internal sealed record RuntimeInspectorSnapshot(
    DateTimeOffset CapturedUtc,
    RuntimeBuildSnapshot Build,
    IReadOnlyList<RuntimeMonitorSnapshot> Monitors,
    IReadOnlyList<RuntimeBrowserSnapshot> Browsers,
    IReadOnlyList<RuntimeUiNodeSnapshot> UiTree);

internal sealed record RuntimeBuildSnapshot(string ProductVersion, string AssemblyVersion, string ExecutablePath, int ProcessId, string Runtime, string OS, string Architecture);
internal sealed record RuntimeMonitorSnapshot(int MonitorId, string? ConversationIdentity, string State, string? PendingMessageId, string? PendingFingerprint, string? DeliveryPhase, int SendAttempts, string? LastReason, DateTimeOffset? LastTransitionUtc);
internal sealed record RuntimeBrowserSnapshot(int ProcessId, string ProcessName, string? MainWindowTitle, bool Responding);
internal sealed record RuntimeUiNodeSnapshot(string Name, string Type, bool Visible, bool Enabled, int X, int Y, int Width, int Height, float DpiX, float DpiY, int Depth);

internal static class RuntimeInspector
{
    public static RuntimeBuildSnapshot CaptureBuild()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var path = Environment.ProcessPath ?? asm.Location;
        return new RuntimeBuildSnapshot(
            FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown",
            asm.GetName().Version?.ToString() ?? "unknown",
            path,
            Environment.ProcessId,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    public static IReadOnlyList<RuntimeBrowserSnapshot> CaptureBrowsers() =>
        Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
            .Select(p => SafeBrowser(p))
            .Where(x => x is not null)
            .Cast<RuntimeBrowserSnapshot>()
            .ToArray();

    private static RuntimeBrowserSnapshot? SafeBrowser(Process p)
    {
        try { return new RuntimeBrowserSnapshot(p.Id, p.ProcessName, p.MainWindowTitle, p.Responding); }
        catch { return null; }
        finally { p.Dispose(); }
    }

    public static IReadOnlyList<RuntimeUiNodeSnapshot> CaptureUiTree(System.Windows.Forms.Control root)
    {
        var result = new List<RuntimeUiNodeSnapshot>();
        Walk(root, 0, result);
        return result;
    }

    private static void Walk(System.Windows.Forms.Control c, int depth, List<RuntimeUiNodeSnapshot> result)
    {
        var dpi = c.DeviceDpi;
        result.Add(new RuntimeUiNodeSnapshot(c.Name, c.GetType().FullName ?? c.GetType().Name, c.Visible, c.Enabled,
            c.Bounds.X, c.Bounds.Y, c.Bounds.Width, c.Bounds.Height, dpi, dpi, depth));
        foreach (System.Windows.Forms.Control child in c.Controls) Walk(child, depth + 1, result);
    }
}
