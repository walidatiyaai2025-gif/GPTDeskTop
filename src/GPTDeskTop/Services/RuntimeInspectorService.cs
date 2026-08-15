using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services;

internal sealed record FieldRuntimeSnapshot(
    DateTimeOffset CapturedUtc,
    object Build,
    IReadOnlyList<object> Monitors,
    IReadOnlyList<object> Browsers,
    IReadOnlyList<object> Ui,
    IReadOnlyList<object> Workers);

internal static class RuntimeInspectorService
{
    public static FieldRuntimeSnapshot Capture(Form owner, ChatGptMonitorService monitor)
    {
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
        var browsers = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
            .Select(p => SafeProcess(p))
            .Where(x => x is not null)
            .Cast<object>()
            .ToArray();
        var ui = new List<object>();
        Walk(owner, 0, ui);
        WalkToolStrips(owner, ui);
        var workers = monitors.Select(m => (object)new { Kind = "MonitorWorker", Snapshot = m }).ToArray();
        return new FieldRuntimeSnapshot(DateTimeOffset.UtcNow, build, monitors, browsers, ui, workers);
    }

    public static string ToSanitizedJson(FieldRuntimeSnapshot snapshot) => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

    public static string Summary(FieldRuntimeSnapshot snapshot)
    {
        var build = JsonSerializer.Serialize(snapshot.Build);
        return $"GPTDeskTop Runtime Inspector\r\nCaptured: {snapshot.CapturedUtc:O}\r\nBuild: {build}\r\nMonitors: {snapshot.Monitors.Count}\r\nBrowser processes: {snapshot.Browsers.Count}\r\nUI controls: {snapshot.Ui.Count}\r\n";
    }

    public static string ExportBundle(Form owner, ChatGptMonitorService monitor, string destinationZip)
    {
        var snapshot = Capture(owner, monitor);
        var temp = Path.Combine(Path.GetTempPath(), "GPTDeskTop-Field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "runtime-inspector.json"), ToSanitizedJson(snapshot), new UTF8Encoding(false));
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

    private static IReadOnlyList<object> CaptureMonitorRuntime(ChatGptMonitorService monitor)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var runningField = typeof(ChatGptMonitorService).GetField("_running", flags);
        if (runningField?.GetValue(monitor) is not System.Collections.IDictionary running) return Array.Empty<object>();
        var result = new List<object>();
        foreach (System.Collections.DictionaryEntry entry in running)
        {
            var runtime = entry.Value;
            var worker = runtime?.GetType().GetProperty("Worker")?.GetValue(runtime) as Task;
            result.Add(new
            {
                MonitorId = entry.Key,
                WorkerStatus = worker?.Status.ToString() ?? "unknown",
                IsCompleted = worker?.IsCompleted ?? false,
                IsFaulted = worker?.IsFaulted ?? false,
                CapturedUtc = DateTimeOffset.UtcNow
            });
        }
        return result;
    }

    private static object? SafeProcess(Process process)
    {
        try { return new { ProcessId = process.Id, Name = process.ProcessName, MainWindowTitle = process.MainWindowTitle, Responding = process.Responding }; }
        catch { return null; }
        finally { process.Dispose(); }
    }

    private static void Walk(Control control, int depth, List<object> output)
    {
        output.Add(new
        {
            Kind = "Control",
            control.Name,
            Type = control.GetType().FullName,
            control.Visible,
            control.Enabled,
            Bounds = new { control.Bounds.X, control.Bounds.Y, control.Bounds.Width, control.Bounds.Height },
            Dpi = control.DeviceDpi,
            Depth = depth
        });
        foreach (Control child in control.Controls) Walk(child, depth + 1, output);
    }

    private static void WalkToolStrips(Control root, List<object> output)
    {
        foreach (var strip in DescendantsAndSelf(root).OfType<ToolStrip>())
        {
            foreach (ToolStripItem item in strip.Items)
                WalkToolStripItem(item, depth: 0, output);
        }
    }

    private static void WalkToolStripItem(ToolStripItem item, int depth, List<object> output)
    {
        var owner = item.Owner;
        var bounds = item.Bounds;
        output.Add(new
        {
            Kind = "ToolStripItem",
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
            WalkToolStripItem(child, depth + 1, output);
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
